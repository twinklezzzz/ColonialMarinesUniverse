using System.Linq;
using Content.Server.CMU14.ColonyEconomy;
using Content.Server.Popups;
using Content.Shared.CMU14.CorporateWork;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CMU14.CorporateWork;

/// <summary>
///     One market for the whole round, shared by every markets terminal. Prices drift each tick;
///     a news event names a commodity and a direction one tick before the move, so players who
///     read the news can get in first. Trades cost a fee both ways, so random trading loses money.
/// </summary>
public sealed class CMUMarketsSystem : EntitySystem
{
    [Dependency] private CorporateConsoleSystem _corporate = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(60);
    public const float Fee = 0.03f;
    public const int HoldingsCap = 5000;
    public const int MaxTradeUnits = 50;

    /// <summary>
    ///     How far a news event pushes its commodity on each tick it lasts.
    /// </summary>
    public const float NewsMove = 0.08f;

    private const int NewsTicks = 3;
    private const int NewsKept = 6;
    private const float MeanReversion = 0.05f;
    private const float MinPriceFactor = 0.4f;
    private const float MaxPriceFactor = 2.5f;

    private readonly Dictionary<string, Market> _markets = new();
    private readonly List<CMUMarketNews> _news = new();
    private TimeSpan _nextTick;
    private TimeSpan _nextNews;

    private sealed class Market
    {
        public required CMUMarketCommodityPrototype Commodity;
        public float Price;
        public float PreviousPrice;
        public int Held;
        public float CostBasis;

        /// <summary>
        ///     Signed push per tick from the current news event, and ticks it has left.
        ///     A new event waits one tick before it starts pushing.
        /// </summary>
        public float Push;
        public int PushTicks;
        public bool PushPending;
    }

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
        SubscribeLocalEvent<CMUMarketsTerminalComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<CMUMarketsTerminalComponent, CMUMarketsTradeBuiMsg>(OnTrade);
    }

    private void Reset()
    {
        _markets.Clear();
        _news.Clear();
    }

    private void EnsureMarkets()
    {
        if (_markets.Count > 0)
            return;

        foreach (var commodity in _prototypes.EnumeratePrototypes<CMUMarketCommodityPrototype>().OrderBy(c => c.ID))
        {
            _markets[commodity.ID] = new Market
            {
                Commodity = commodity,
                Price = commodity.BasePrice,
                PreviousPrice = commodity.BasePrice,
            };
        }

        _nextTick = _timing.CurTime + Tick;
        _nextNews = _timing.CurTime + NextNewsDelay();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Only run the market while someone could trade on it.
        var terminals = EntityQueryEnumerator<CMUMarketsTerminalComponent>();
        if (!terminals.MoveNext(out _, out _))
            return;

        EnsureMarkets();
        var now = _timing.CurTime;
        if (now >= _nextNews)
        {
            _nextNews = now + NextNewsDelay();
            StartNews(now);
            UpdateAllUi();
        }

        if (now < _nextTick)
            return;

        _nextTick += Tick;
        foreach (var market in _markets.Values)
        {
            MoveMarket(market);
        }

        UpdateAllUi();
    }

    private TimeSpan NextNewsDelay()
    {
        return TimeSpan.FromSeconds(_random.Next(240, 421));
    }

    private void StartNews(TimeSpan now)
    {
        var market = _random.Pick(_markets.Values.ToList());
        var up = _random.Prob(0.5f);
        market.Push = up ? NewsMove : -NewsMove;
        market.PushTicks = NewsTicks;
        market.PushPending = true;

        var headline = Loc.GetString(up ? "cmu-markets-news-up" : "cmu-markets-news-down",
            ("commodity", Loc.GetString(market.Commodity.Name)),
            ("story", Loc.GetString($"cmu-markets-story-{(up ? "up" : "down")}-{_random.Next(1, 5)}")));
        _news.Insert(0, new CMUMarketNews(now, headline));
        if (_news.Count > NewsKept)
            _news.RemoveAt(_news.Count - 1);
    }

    private void MoveMarket(Market market)
    {
        var basePrice = market.Commodity.BasePrice;
        var change = _random.NextFloat(-market.Commodity.Volatility, market.Commodity.Volatility);
        change += MeanReversion * (basePrice - market.Price) / basePrice;

        if (market.PushPending)
        {
            market.PushPending = false;
        }
        else if (market.PushTicks > 0)
        {
            change += market.Push;
            market.PushTicks--;
        }

        market.PreviousPrice = market.Price;
        market.Price = Math.Clamp(market.Price * (1 + change), basePrice * MinPriceFactor, basePrice * MaxPriceFactor);
    }

    private void OnUiOpened(Entity<CMUMarketsTerminalComponent> ent, ref BoundUIOpenedEvent args)
    {
        EnsureMarkets();
        UpdateUi(ent);
    }

    private void OnTrade(Entity<CMUMarketsTerminalComponent> ent, ref CMUMarketsTradeBuiMsg args)
    {
        TryTrade(args.Commodity, args.Units, args.Buy, ent, args.Actor);
    }

    /// <summary>
    ///     Buys or sells <paramref name="units"/> with the corporate budget. Returns the money moved
    ///     (negative when buying), or null if the trade was refused.
    /// </summary>
    public int? TryTrade(string commodity, int units, bool buy, EntityUid terminal, EntityUid user)
    {
        EnsureMarkets();
        if (!_markets.TryGetValue(commodity, out var market) || units <= 0)
            return null;

        units = Math.Min(units, MaxTradeUnits);
        var price = (int) MathF.Round(market.Price);
        int moved;

        if (buy)
        {
            var cost = (int) MathF.Ceiling(units * price * (1 + Fee));
            if (HoldingsValue() + units * price > HoldingsCap)
            {
                _popup.PopupEntity(Loc.GetString("cmu-markets-refuse-cap", ("cap", HoldingsCap)), terminal, user);
                return null;
            }

            if (_corporate.GetCorporateBudget() < cost)
            {
                _popup.PopupEntity(Loc.GetString("cmu-markets-refuse-funds"), terminal, user);
                return null;
            }

            market.Held += units;
            market.CostBasis += cost;
            moved = -cost;
        }
        else
        {
            if (market.Held < units)
            {
                _popup.PopupEntity(Loc.GetString("cmu-markets-refuse-held"), terminal, user);
                return null;
            }

            market.CostBasis -= market.CostBasis * units / market.Held;
            market.Held -= units;
            moved = (int) MathF.Floor(units * price * (1 - Fee));
        }

        _corporate.AddToCorporateBudget(moved);
        UpdateAllUi();
        return moved;
    }

    private int HoldingsValue()
    {
        return (int) _markets.Values.Sum(m => m.Held * m.Price);
    }

    /// <summary>
    ///     Current price of a commodity, for tests and admin tools.
    /// </summary>
    public int? GetPrice(string commodity)
    {
        EnsureMarkets();
        return _markets.TryGetValue(commodity, out var market) ? (int) MathF.Round(market.Price) : null;
    }

    private void UpdateAllUi()
    {
        var query = EntityQueryEnumerator<CMUMarketsTerminalComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, CMUMarketsTerminalUi.Key))
                UpdateUi((uid, comp));
        }
    }

    private void UpdateUi(Entity<CMUMarketsTerminalComponent> ent)
    {
        var quotes = _markets.Values
            .Select(m => new CMUMarketQuote(
                m.Commodity.ID,
                m.Commodity.Name,
                (int) MathF.Round(m.Price),
                (int) MathF.Round(m.PreviousPrice),
                m.Held,
                m.Held > 0 ? (int) MathF.Round(m.CostBasis / m.Held) : 0))
            .ToList();

        var state = new CMUMarketsTerminalBuiState(
            quotes,
            _news.ToList(),
            (int) _corporate.GetCorporateBudget(),
            HoldingsValue(),
            HoldingsCap,
            (int) (Fee * 100),
            _nextTick);

        _ui.SetUiState(ent.Owner, CMUMarketsTerminalUi.Key, state);
    }
}

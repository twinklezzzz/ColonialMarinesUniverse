using System.Linq;
using Content.Server.Mind;
using Content.Shared.CMU14.CorporateWork;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Random;

namespace Content.Server.CMU14.CorporateWork;

/// <summary>
///     What happens in the round moves the stock of the companies in it. Research income lifts a
///     company, staff deaths drag it down, and a big setback (a withdrawal, a lost research licence)
///     tanks it, while a rival in the same line of business that isn't in the round moves in to
///     fill the gap. Companies not in the round only follow the market and the newswire.
/// </summary>
public sealed partial class CMUMarketsSystem
{
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    /// <summary>
    ///     Stock change when one of a company's people dies.
    /// </summary>
    public const float DeathShock = -0.02f;

    /// <summary>
    ///     Stock change per $100 of research income, up to <see cref="MaxIncomeShock"/> at once.
    /// </summary>
    public const float IncomeShockPer100 = 0.004f;
    public const float MaxIncomeShock = 0.05f;

    /// <summary>
    ///     How much a rival gains moving in after a company's setback.
    /// </summary>
    public const float GapFill = 0.12f;

    private void InitializeEvents()
    {
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (!_started || args.NewMobState != MobState.Dead || args.OldMobState == MobState.Dead)
            return;

        if (GetForce(args.Target) is not { } force || StockFor(force) is not { } stock)
            return;

        Shock(stock.Commodity.ID, DeathShock);
    }

    /// <summary>
    ///     A company in the round earned <paramref name="amount"/>. Its stock rises, and rivals in
    ///     its sectors dip a little.
    /// </summary>
    public void ReportIncome(string force, float amount)
    {
        if (!_started || amount <= 0 || StockFor(force) is not { } stock)
            return;

        var change = MathF.Min(MaxIncomeShock, amount / 100 * IncomeShockPer100);
        Shock(stock.Commodity.ID, change);
        foreach (var rival in Rivals(stock))
            Shock(rival.Commodity.ID, -change / 4);
    }

    /// <summary>
    ///     A company in the round took a hit in <paramref name="sector"/>. Its stock drops by
    ///     <paramref name="change"/> and a rival in that sector not in the round fills the gap.
    ///     Both make the newswire.
    /// </summary>
    public void ReportSetback(string force, float change, string sector, LocId reason)
    {
        EnsureMarkets();
        if (StockFor(force) is not { } stock)
            return;

        Shock(stock.Commodity.ID, change);
        var now = _timing.CurTime;
        AddNews(now, now, Loc.GetString("cmu-markets-news-setback",
            ("symbol", stock.Commodity.Symbol),
            ("company", Loc.GetString(stock.Commodity.Name)),
            ("reason", Loc.GetString(reason))), CMUMarketKind.Stock);

        var rivals = Rivals(stock, sector).Where(r => !IsInRound(r)).ToList();
        if (rivals.Count == 0)
            return;

        var rival = _random.Pick(rivals);
        Shock(rival.Commodity.ID, GapFill);
        AddNews(now, now, Loc.GetString("cmu-markets-news-gap",
            ("symbol", rival.Commodity.Symbol),
            ("company", Loc.GetString(rival.Commodity.Name)),
            ("rival", Loc.GetString(stock.Commodity.Name))), CMUMarketKind.Stock);
        UpdateAllUi();
    }

    private Market? StockFor(string force)
    {
        EnsureMarkets();
        return _markets.Values.FirstOrDefault(m => m.Commodity.Kind == CMUMarketKind.Stock && m.Commodity.RoundForces.Contains(force));
    }

    private IEnumerable<Market> Rivals(Market company, string? sector = null)
    {
        return _markets.Values.Where(m =>
            m != company &&
            m.Commodity.Kind == CMUMarketKind.Stock &&
            (sector == null ? m.Commodity.Sectors.Intersect(company.Commodity.Sectors).Any() : m.Commodity.Sectors.Contains(sector)));
    }

    private string? GetForce(EntityUid uid)
    {
        if (!_mind.TryGetMind(uid, out var mindId, out _) || !_jobs.MindTryGetJob(mindId, out var job))
            return null;

        return job.RoundForce;
    }

    /// <summary>
    ///     Whether anyone alive in the round works for this company.
    /// </summary>
    public bool IsInRound(string commodity)
    {
        return _markets.TryGetValue(commodity, out var market) && IsInRound(market);
    }

    private bool IsInRound(Market company)
    {
        if (company.Commodity.RoundForces.Count == 0)
            return false;

        var minds = EntityQueryEnumerator<MindComponent>();
        while (minds.MoveNext(out var mindId, out var mind))
        {
            if (mind.OwnedEntity is not { } body || _mobState.IsDead(body))
                continue;

            if (_jobs.MindTryGetJob(mindId, out var job) && job.RoundForce is { } force && company.Commodity.RoundForces.Contains(force))
                return true;
        }

        return false;
    }
}

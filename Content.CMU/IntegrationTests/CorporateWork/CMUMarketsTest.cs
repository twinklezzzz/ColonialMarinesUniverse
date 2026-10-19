using Content.Server.CMU14.ColonyEconomy;
using Content.Server.CMU14.CorporateWork;
using Content.Shared.Access.Components;
using Content.Shared.CMU14.CorporateWork;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.CMU14.CorporateWork;

[TestFixture]
[TestOf(typeof(CMUMarketsSystem))]
public sealed class CMUMarketsTest
{
    private const string Commodity = "CMUMarketTitanium";

    [Test]
    public async Task TradesMovePricesChargeFeesAndRespectCaps()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var markets = server.System<CMUMarketsSystem>();
            var corporate = server.System<CorporateConsoleSystem>();
            var terminal = entities.SpawnEntity("CMUCorporateMarketsTerminal", map.GridCoords);
            var desktop = entities.SpawnEntity("RMCPersonalDesktop", map.GridCoords);
            var console = entities.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            var card = entities.SpawnEntity("AU14IDCardColonySynthetic", map.GridCoords);
            try
            {
                corporate.AddToCorporateBudget(100000);
                var wy = markets.GetTrader((terminal, entities.GetComponent<CMUMarketsTerminalComponent>(terminal)), card)!;
                Assert.That(wy.Corporate, Is.True);
                Assert.That(wy.Cap, Is.EqualTo(CMUMarketsSystem.CorporateHoldingsCap));

                // Nothing held yet, so nothing to sell.
                Assert.That(markets.TryTrade(Commodity, 1, false, wy, terminal, card), Is.Null);

                // A buy pushes the price up, and selling straight back loses the fees.
                var before = markets.GetPrice(Commodity)!.Value;
                var budget = corporate.GetCorporateBudget();
                var bought = markets.TryTrade(Commodity, 40, true, wy, terminal, card)!.Value;
                Assert.That(markets.GetPrice(Commodity)!.Value, Is.GreaterThan(before));
                Assert.That(corporate.GetCorporateBudget(), Is.EqualTo(budget + bought).Within(0.5f));
                Assert.That(markets.PayDividends(), Is.GreaterThan(0));
                var sold = markets.TryTrade(Commodity, 40, false, wy, terminal, card)!.Value;
                Assert.That(sold + bought, Is.LessThan(0));

                // The civilian app trades the card's bank account, under the smaller cap.
                var id = entities.GetComponent<IdCardComponent>(card);
                id.AccountBalance = 5000;
                var civ = markets.GetTrader((desktop, entities.GetComponent<CMUMarketsTerminalComponent>(desktop)), card)!;
                Assert.That(civ.Corporate, Is.False);
                Assert.That(civ.Cap, Is.EqualTo(CMUMarketsSystem.CivilianHoldingsCap));
                Assert.That(markets.TryTrade("CMUMarketSynthetics", CMUMarketsSystem.MaxTradeUnits, true, civ, desktop, card), Is.Null, "A civilian went past the holdings cap.");

                var paid = markets.TryTrade(Commodity, 10, true, civ, desktop, card)!.Value;
                Assert.That(id.AccountBalance, Is.EqualTo(5000 + paid));
                Assert.That(markets.HoldingsValue(civ.Key), Is.GreaterThan(0));
                Assert.That(markets.HoldingsValue(wy.Key), Is.Zero, "Civilian and corporate holdings were mixed.");

                // Company stock is for colonists only.
                Assert.That(markets.TryTrade("CMUStockArmat", 1, true, wy, terminal, card), Is.Null, "The corporate terminal bought stock.");
                Assert.That(markets.TryTrade("CMUStockArmat", 1, true, civ, desktop, card), Is.Not.Null);

                // Corporate events move the company's own stock.
                var stock = markets.GetPrice("CMUStockWeylandYutani")!.Value;
                markets.Shock("CMUStockWeylandYutani", -0.15f);
                Assert.That(markets.GetPrice("CMUStockWeylandYutani")!.Value, Is.EqualTo(stock * 0.85f).Within(0.01f));

                // An empty account can't buy.
                id.AccountBalance = 0;
                Assert.That(markets.TryTrade(Commodity, 1, true, civ, desktop, card), Is.Null);
            }
            finally
            {
                entities.DeleteEntity(terminal);
                entities.DeleteEntity(desktop);
                entities.DeleteEntity(console);
                entities.DeleteEntity(card);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CivilianNewsArrivesShortlyBeforeTheMove()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var markets = server.System<CMUMarketsSystem>();
        EntityUid terminal = default;
        TimeSpan movesAt = default;
        await server.WaitAssertion(() =>
        {
            terminal = server.EntMan.SpawnEntity("CMUCorporateMarketsTerminal", map.GridCoords);
            movesAt = markets.BreakNews(Commodity, true);
            Assert.That(markets.GetVisibleNews(true), Is.Not.Empty, "The corporate terminal didn't get the news as it broke.");
            Assert.That(markets.GetVisibleNews(false), Is.Empty, "Civilians got the news early.");
        });

        var untilCivilian = movesAt - CMUMarketsSystem.CivilianNewsLead - server.Timing.CurTime;
        await server.WaitRunTicks((int) (untilCivilian.TotalSeconds / server.Timing.TickPeriod.TotalSeconds) + 2);
        await server.WaitAssertion(() =>
        {
            Assert.That(markets.GetVisibleNews(false), Is.Not.Empty, "Civilians never got the news.");
            Assert.That(server.Timing.CurTime, Is.LessThan(movesAt), "Civilians only got the news after the move started.");
            server.EntMan.DeleteEntity(terminal);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundEventsMoveCompanyStock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var markets = server.System<CMUMarketsSystem>();
            var terminal = server.EntMan.SpawnEntity("RMCPersonalDesktop", map.GridCoords);
            try
            {
                const string wy = "CMUStockWeylandYutani";
                const string chigusa = "CMUStockChigusa";
                markets.GetPrice(wy);

                // Research income lifts the company that earned it.
                var before = markets.GetPrice(wy)!.Value;
                markets.ReportIncome("WY", 1000);
                Assert.That(markets.GetPrice(wy)!.Value, Is.EqualTo(before * (1 + 10 * CMUMarketsSystem.IncomeShockPer100)).Within(0.05f));

                // Withdrawing halves the stock, and a rival in the sector that isn't in the round fills the gap.
                before = markets.GetPrice(wy)!.Value;
                var rivalBefore = markets.GetPrice(chigusa)!.Value;
                Assert.That(markets.IsInRound(chigusa), Is.False);
                markets.ReportSetback("WY", -0.5f, "Colonization", "cmu-markets-setback-withdrawal");
                Assert.Multiple(() =>
                {
                    Assert.That(markets.GetPrice(wy)!.Value, Is.EqualTo(before * 0.5f).Within(0.05f));
                    Assert.That(markets.GetPrice(chigusa)!.Value, Is.EqualTo(rivalBefore * (1 + CMUMarketsSystem.GapFill)).Within(0.05f));
                    Assert.That(markets.GetVisibleNews(false), Has.Count.EqualTo(2), "Colonists didn't hear about the setback at once.");
                    Assert.That(markets.GetVisibleNews(true), Is.Empty, "Stock news reached the corporate terminal.");
                });
            }
            finally
            {
                server.EntMan.DeleteEntity(terminal);
            }
        });
        await pair.CleanReturnAsync();
    }
}

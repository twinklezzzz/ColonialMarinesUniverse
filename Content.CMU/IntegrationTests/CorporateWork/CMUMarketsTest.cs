using Content.Server.CMU14.ColonyEconomy;
using Content.Server.CMU14.CorporateWork;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.CMU14.CorporateWork;

[TestFixture]
[TestOf(typeof(CMUMarketsSystem))]
public sealed class CMUMarketsTest
{
    private const string Commodity = "CMUMarketTitanium";

    [Test]
    public async Task TradesChargeTheFeeAndRespectLimits()
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
            var console = entities.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            var trader = entities.SpawnEntity("CMMobHuman", map.GridCoords);
            try
            {
                corporate.AddToCorporateBudget(100000);
                var price = markets.GetPrice(Commodity)!.Value;

                // Nothing held yet, so nothing to sell.
                Assert.That(markets.TryTrade(Commodity, 1, false, terminal, trader), Is.Null);

                var budget = corporate.GetCorporateBudget();
                var bought = markets.TryTrade(Commodity, 10, true, terminal, trader)!.Value;
                Assert.That(-bought, Is.EqualTo((int) MathF.Ceiling(10 * price * (1 + CMUMarketsSystem.Fee))));
                Assert.That(corporate.GetCorporateBudget(), Is.EqualTo(budget + bought).Within(0.5f));

                // Selling straight back at the same price loses the fee both ways.
                var sold = markets.TryTrade(Commodity, 10, false, terminal, trader)!.Value;
                Assert.That(sold + bought, Is.LessThan(0));

                // Buying past the holdings cap is refused.
                Assert.That(markets.TryTrade("CMUMarketSynthetics", CMUMarketsSystem.MaxTradeUnits, true, terminal, trader), Is.Null);
            }
            finally
            {
                entities.DeleteEntity(terminal);
                entities.DeleteEntity(console);
                entities.DeleteEntity(trader);
            }
        });
        await pair.CleanReturnAsync();
    }
}

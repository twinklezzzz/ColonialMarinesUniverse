using Content.Server.CMU14.Ambassador;
using Content.Server.CMU14.ColonyEconomy;
using Content.Server.CMU14.CorporateRelations;
using Content.Shared._RMC14.Marines.Roles.Ranks;
using Content.Shared.CMU14.CorporateRelations;
using Content.Shared.GameTicking;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.CMU14.CorporateRelations;

[TestFixture]
[TestOf(typeof(CMUCorporateRelationsSystem))]
public sealed class CMUCorporateRelationsTest
{
    private const string Faction = "UPP";

    [Test]
    public async Task LoansRentWorkOrdersAndWithdrawalMoveTheRightMoney()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var relations = server.System<CMUCorporateRelationsSystem>();
        var corporate = server.System<CorporateConsoleSystem>();
        var colony = server.System<ColonyBudgetSystem>();
        var ambassador = server.System<AmbassadorConsoleSystem>();

        EntityUid adminConsole = default, corporateConsole = default, ambassadorConsole = default, user = default;
        await server.WaitPost(() =>
        {
            entities.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
            adminConsole = entities.SpawnEntity("AU14AdminConsole", map.GridCoords);
            corporateConsole = entities.SpawnEntity("AU14CorporateConsole", map.GridCoords);
            ambassadorConsole = entities.SpawnEntity("AU14AmbassadorConsoleUPP", map.GridCoords);
            user = entities.SpawnEntity("CMMobHuman", map.GridCoords);
            corporate.AddToCorporateBudget(10000 - corporate.GetCorporateBudget());
            colony.SetBudget(5000);
        });

        // The first tick with a colony starts the cycle and its existing debt.
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(relations.GetOwed(CMUCorporateRelationsSystem.ColonyBorrower), Is.EqualTo(CMUCorporateRelationsSystem.StartingDebt));
            var ambassadorStart = ambassador.GetFactionBudget(Faction);

            // UPP asks for $2000, WY counters at 20%, UPP accepts.
            Raise(ambassadorConsole, new CMUContractsRequestLoanBuiMsg(2000));
            Raise(corporateConsole, new CMUContractsCounterLoanBuiMsg(Faction, 2000, 20));
            Raise(ambassadorConsole, new CMUContractsAnswerLoanBuiMsg(Faction, true));
            Assert.Multiple(() =>
            {
                Assert.That(ambassador.GetFactionBudget(Faction), Is.EqualTo(ambassadorStart + 2000));
                Assert.That(corporate.GetCorporateBudget(), Is.EqualTo(8000));
                Assert.That(relations.GetOwed(Faction), Is.EqualTo(2400));
            });

            // A work order for 10 steel ore at +20%: $20 held, then paid to the colony on delivery
            // while WY takes half the delivery's value as resale margin.
            Raise(corporateConsole, new CMUContractsPostWorkOrderBuiMsg("CMUWorkOrderSteelOre", 10, 20));
            Assert.That(corporate.GetCorporateBudget(), Is.EqualTo(7980));
            var ore = entities.SpawnEntity(null, map.GridCoords);
            server.System<TagSystem>().AddTag(ore, "SteelOre");
            relations.OnDelivery(ore, 10, 100);
            Assert.Multiple(() =>
            {
                Assert.That(colony.GetBudget(), Is.EqualTo(5020));
                Assert.That(corporate.GetCorporateBudget(), Is.EqualTo(8030));
            });
            entities.DeleteEntity(ore);

            // Rent and instalments: $500 rent to the colony, a third of each loan back to WY.
            Raise(adminConsole, new CMUContractsSetRentBuiMsg(500));
            relations.RunCycle();
            Assert.Multiple(() =>
            {
                Assert.That(colony.GetBudget(), Is.EqualTo(5020 + 500 - 500));
                Assert.That(corporate.GetCorporateBudget(), Is.EqualTo(8030 - 500 + 500 + 800));
                Assert.That(relations.GetOwed(CMUCorporateRelationsSystem.ColonyBorrower), Is.EqualTo(1000));
                Assert.That(relations.GetOwed(Faction), Is.EqualTo(1600));
            });

            // Only WYC6 and above can order the withdrawal.
            server.System<SharedRankSystem>().SetRank(user, "RMCRankWeYaExecutiveSpecialist");
            Raise(corporateConsole, new CMUContractsWithdrawalBuiMsg(true));
            Assert.That(relations.Withdrawn, Is.False);

            // Withdrawal collects every loan on the spot and turns the colony's prices up.
            relations.Withdraw();
            Assert.Multiple(() =>
            {
                Assert.That(relations.Withdrawn, Is.True);
                Assert.That(relations.GetOwed(CMUCorporateRelationsSystem.ColonyBorrower), Is.Zero);
                Assert.That(relations.GetOwed(Faction), Is.Zero);
                Assert.That(relations.ColonyPriceMultiplier, Is.EqualTo(1.25f));
                Assert.That(relations.ColonyPayoutMultiplier, Is.EqualTo(0.75f));
            });

            entities.DeleteEntity(adminConsole);
            entities.DeleteEntity(corporateConsole);
            entities.DeleteEntity(ambassadorConsole);
            entities.DeleteEntity(user);
            entities.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
        });

        await pair.CleanReturnAsync();

        void Raise(EntityUid console, BoundUserInterfaceMessage message)
        {
            message.Actor = user;
            // As object, so the bus dispatches on the message's own type.
            entities.EventBus.RaiseLocalEvent(console, (object) message);
        }
    }
}

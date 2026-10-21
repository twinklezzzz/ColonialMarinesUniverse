using System.Linq;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.CMU14.Specimens;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.CMU14.Specimens;

[TestFixture]
[TestOf(typeof(CMUSpecimenCaptureSystem))]
public sealed class CMUSpecimenCaptureTest
{
    [TestCase("CMXenoDrone")]
    [TestCase("CMU14XenoNeomorph")]
    [TestCase("AU14Wendigo")]
    [TestCase("CMUWendigoLab")]
    [TestCase("CMUXenoAbomination")]
    [TestCase("AU14BiomorphGrunt")]
    [TestCase("AU14BiomorphSpider")]
    public async Task SedateRestrainContainAndBreakFree(string creature)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var capture = server.System<CMUSpecimenCaptureSystem>();
        var container = server.System<SharedContainerSystem>();
        var actions = server.System<SharedActionsSystem>();

        // Whether every one of the creature's abilities is refused, acid included.
        bool ActionsBlocked(EntityUid uid)
        {
            foreach (var action in actions.GetActions(uid))
            {
                var ev = new ActionAttemptEvent(uid);
                entities.EventBus.RaiseLocalEvent(action.Owner, ref ev);
                if (!ev.Cancelled)
                    return false;
            }

            return true;
        }

        EntityUid xeno = default;
        EntityUid crate = default;
        await server.WaitAssertion(() =>
        {
            xeno = entities.SpawnEntity(creature, map.GridCoords);
            Assert.That(capture.IsSpecimen(xeno), Is.True);

            // One hit short of the threshold leaves it standing; the next one puts it under.
            var threshold = (int) capture.GetSedationThreshold(xeno);
            for (var i = 0; i < threshold - 1; i++)
                Assert.That(capture.Sedate(xeno, 1, TimeSpan.FromSeconds(30)), Is.False);

            Assert.That(capture.Sedate(xeno, 1, TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(capture.IsSubdued(xeno), Is.True);

            capture.Restrain(xeno, TimeSpan.FromSeconds(60));
        });

        // Well past any ordinary xeno stun, the restraint still holds it down.
        await server.WaitRunTicks((int) (45 / server.Timing.TickPeriod.TotalSeconds));
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<CMUSpecimenRestrainedComponent>(xeno), Is.True);
            Assert.That(capture.IsSubdued(xeno), Is.True);
            Assert.That(ActionsBlocked(xeno), Is.True, "A harnessed specimen can still use its abilities.");

            crate = entities.SpawnEntity("CMUSpecimenCrate", map.GridCoords);
            var crateEnt = new Entity<CMUSpecimenCrateComponent>(crate, entities.GetComponent<CMUSpecimenCrateComponent>(crate));

            // A crate is set up open; loading it doesn't need the lid moving.
            Assert.That(crateEnt.Comp.State, Is.EqualTo(CMUSpecimenCrateState.Open));
            Assert.That(capture.TryContain(crateEnt, xeno), Is.True);
            Assert.That(container.IsEntityInContainer(xeno), Is.True);
            Assert.That(capture.GetSpecimen(crateEnt), Is.EqualTo(xeno));

            // Closing runs the lid animation, then seals it.
            Assert.That(capture.TryToggle(crateEnt), Is.True);
            Assert.That(crateEnt.Comp.State, Is.EqualTo(CMUSpecimenCrateState.Closing));
            Assert.That(capture.TryToggle(crateEnt), Is.False, "The lid reversed mid-swing.");
        });

        // Crated, the breakout timer is paused.
        await server.WaitRunTicks((int) (40 / server.Timing.TickPeriod.TotalSeconds));
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<CMUSpecimenRestrainedComponent>(xeno), Is.True, "A crated specimen broke free.");
            Assert.That(ActionsBlocked(xeno), Is.True, "A crated specimen can still use its abilities.");
            Assert.That(entities.GetComponent<CMUSpecimenCrateComponent>(crate).State, Is.EqualTo(CMUSpecimenCrateState.Closed), "The lid never finished closing.");

            // Destroying the crate drops the specimen instead of deleting it.
            entities.DeleteEntity(crate);
            Assert.That(entities.EntityExists(xeno), Is.True);
            Assert.That(container.IsEntityInContainer(xeno), Is.False);
        });

        // Out of the crate, the harness gives way.
        await server.WaitRunTicks((int) (40 / server.Timing.TickPeriod.TotalSeconds));
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<CMUSpecimenRestrainedComponent>(xeno), Is.False);
            Assert.That(capture.IsHeld(xeno), Is.False);
            entities.DeleteEntity(xeno);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpawnerPlacesCrate()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var spawner = entities.SpawnEntity("CMUSpawnSpecimenCrate", map.GridCoords);
            var crates = entities.EntityQuery<CMUSpecimenCrateComponent>().ToList();
            Assert.That(crates, Has.Count.EqualTo(1), "The spawner didn't place a crate.");
            Assert.That(crates[0].State, Is.EqualTo(CMUSpecimenCrateState.Open));

            entities.DeleteEntity(crates[0].Owner);
            entities.DeleteEntity(spawner);
        });

        await pair.CleanReturnAsync();
    }
}

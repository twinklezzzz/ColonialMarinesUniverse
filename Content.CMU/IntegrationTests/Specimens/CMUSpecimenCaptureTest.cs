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
            entities.DeleteEntity(xeno);
        });

        await pair.CleanReturnAsync();
    }
}

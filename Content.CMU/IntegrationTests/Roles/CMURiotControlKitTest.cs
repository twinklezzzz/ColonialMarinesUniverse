using System.Linq;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Station;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.CMU14.Roles;

[TestFixture]
public sealed class CMURiotControlKitTest
{
    private static readonly string[] ArmourAccessories =
    {
        "CMUHelmetGarbPMCGreaves",
        "CMUHelmetGarbPMCKneepads",
        "CMUHelmetGarbPMCPads",
        "CMUHelmetGarbPMCBraces",
    };

    [TestCase("CMUGearWYRiotSpecialist", "CMUWeaponShotgunM890RiotFilled")]
    [TestCase("CMUGearWYRiotLeader", "RMCWeaponSMGM63B2")]
    [TestCase("CMUGearWYRiotGrenadier", "CMUWeaponLauncherM81")]
    public async Task RiotKitIsFullyFitted(string gear, string weapon)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();
            var containers = server.System<SharedContainerSystem>();
            var mob = entities.SpawnEntity("CMMobHuman", map.GridCoords);
            server.System<SharedStationSpawningSystem>().EquipStartingGear(mob, gear);

            string? Proto(EntityUid uid) => entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
            List<string?> Stored(EntityUid uid) => containers.GetContainer(uid, StorageComponent.ContainerId).ContainedEntities.Select(Proto).ToList();

            Assert.That(inventory.TryGetSlotEntity(mob, "outerClothing", out var armour), Is.True);
            Assert.That(Stored(armour!.Value), Is.SupersetOf(ArmourAccessories), "The armour is missing its accessories.");

            Assert.That(inventory.TryGetSlotEntity(mob, "head", out var helmet), Is.True);
            Assert.That(Stored(helmet!.Value), Does.Contain("AU14HelmetGarbRiotVisor"), "The helmet is missing its visor.");
            Assert.That(Stored(helmet!.Value), Does.Not.Contain("RMCRC6RiotShield"), "The helmet has a second face shield.");

            Assert.That(inventory.TryGetSlotEntity(mob, "mask", out var mask), Is.True, "No gas mask.");
            Assert.That(server.System<ItemSlotsSystem>().GetItemOrNull(mask!.Value, "filter") is { } filter ? Proto(filter) : null,
                Is.EqualTo("CMUGasMaskFilterWY"), "The gas mask has no filter.");

            Assert.That(inventory.TryGetSlotEntity(mob, "back", out var bag), Is.True);
            Assert.That(Stored(bag!.Value), Does.Contain("AU14TeleBaton"));

            Assert.That(inventory.TryGetSlotEntity(mob, "suitstorage", out var gun), Is.True);
            Assert.That(Proto(gun!.Value), Is.EqualTo(weapon));

            entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("CMUGearWYSecurityTeamOfficer", "RMCJumpsuitVeteranPMCKutjevo")]
    [TestCase("CMUGearWYSecurityTeamSergeant", "RMCJumpsuitVeteranPMCKutjevoLead")]
    public async Task SecurityTeamKitIsArmed(string gear, string uniform)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();
            var containers = server.System<SharedContainerSystem>();
            var mob = entities.SpawnEntity("CMMobHuman", map.GridCoords);
            server.System<SharedStationSpawningSystem>().EquipStartingGear(mob, gear);

            string? Proto(EntityUid uid) => entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
            string? Worn(string slot) => inventory.TryGetSlotEntity(mob, slot, out var item) ? Proto(item.Value) : null;

            Assert.That(Worn("jumpsuit"), Is.EqualTo(uniform));
            Assert.That(Worn("belt"), Is.EqualTo("RMCM77BeltPMCFilled"));
            Assert.That(Worn("suitstorage"), Is.EqualTo("RMCWeaponRifleSSG45"));

            Assert.That(inventory.TryGetSlotEntity(mob, "back", out var bag), Is.True);
            var stored = containers.GetContainer(bag!.Value, StorageComponent.ContainerId).ContainedEntities.Select(Proto).ToList();
            Assert.That(stored, Is.SupersetOf(new[] { "RMCWeaponTaser", "CMStunbaton", "RMCHandcuffs" }), "The satchel is missing the taser, baton or cuffs.");

            entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("CMUGearWYBioTechnician", "suitstorage", "RMCWeaponRifleM54C2")]
    [TestCase("CMUGearWYBioTechnician", "outerClothing", "CMUSuitBioWYSec")]
    [TestCase("CMUGearWYBioTechnician", "head", "CMUHoodBioWeYaFilled")]
    [TestCase("CMUGearWYBioSterilizer", "back", "RMCWeaponFlamer")]
    [TestCase("CMUGearWYBioSterilizer", "pocket1", "RMCPouchFuelTank")]
    [TestCase("CMUGearWYBioLeader", "back", "RMCWeaponFlamer")]
    [TestCase("CMUGearWYBioLeader", "suitstorage", "RMCWeaponRifleM54C2")]
    [TestCase("CMUGearWYBioMedic", "belt", "AU14BeltMedicBagSupportSynth")]
    [TestCase("CMUGearWYIAAuditor", "jumpsuit", "CMUJumpsuitLiaisonHolsterM77")]
    [TestCase("CMUGearWYIASeniorAuditor", "pocket1", "CMUMobilePhone")]
    [TestCase("CMUGearWYRecoveryTechnician", "suitstorage", "CMUWeaponShotgunXM38Capture")]
    [TestCase("CMUGearWYRecoveryOperator", "suitstorage", "RMCWeaponRifleM54C2")]
    [TestCase("CMUGearWYRecoveryLeader", "suitstorage", "RMCWeaponRifleM54C2")]
    [TestCase("CMUGearWYRecoveryMedic", "belt", "AU14BeltMedicBagSupportSynth")]
    public async Task NewCorporateTeamKitsAreWorn(string gear, string slot, string expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();
            var mob = entities.SpawnEntity("CMMobHuman", map.GridCoords);
            server.System<SharedStationSpawningSystem>().EquipStartingGear(mob, gear);

            Assert.That(inventory.TryGetSlotEntity(mob, slot, out var item), Is.True, $"{gear} left {slot} empty.");
            Assert.That(entities.GetComponent<MetaDataComponent>(item!.Value).EntityPrototype?.ID, Is.EqualTo(expected));

            entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     The bio hood doubles as the unit's gas mask, so it comes with a filter fitted.
    ///     The auditors' M77 is in a shoulder holster under the jacket, not on a belt.
    /// </summary>
    [Test]
    public async Task HoodFilterAndShoulderHolster()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();
            var containers = server.System<SharedContainerSystem>();
            string? Proto(EntityUid uid) => entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;

            var tech = entities.SpawnEntity("CMMobHuman", map.GridCoords);
            server.System<SharedStationSpawningSystem>().EquipStartingGear(tech, "CMUGearWYBioTechnician");
            Assert.That(inventory.TryGetSlotEntity(tech, "head", out var hood), Is.True);
            Assert.That(server.System<ItemSlotsSystem>().GetItemOrNull(hood!.Value, "filter") is { } filter ? Proto(filter) : null,
                Is.EqualTo("CMUGasMaskFilterWY"), "The bio hood has no filter.");

            var auditor = entities.SpawnEntity("CMMobHuman", map.GridCoords);
            server.System<SharedStationSpawningSystem>().EquipStartingGear(auditor, "CMUGearWYIAAuditor");
            Assert.That(inventory.TryGetSlotEntity(auditor, "belt", out _), Is.False, "The auditor wears a belt holster.");
            Assert.That(inventory.TryGetSlotEntity(auditor, "jumpsuit", out var suit), Is.True);
            var webbing = entities.GetComponent<Content.Shared._RMC14.Webbing.WebbingClothingComponent>(suit!.Value).Webbing;
            Assert.That(webbing, Is.Not.Null, "The suit has no shoulder holster.");
            var held = containers.GetContainer(webbing!.Value, StorageComponent.ContainerId).ContainedEntities.Select(Proto).ToList();
            Assert.That(held, Does.Contain("RMCWeaponPistolM77"));

            entities.DeleteEntity(tech);
            entities.DeleteEntity(auditor);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     Every Weyland-Yutani third-party role arrives on the WY distress headset.
    /// </summary>
    [Test]
    public async Task WeylandThirdPartiesUseTheWeYaHeadset()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var checkedJobs = 0;
            foreach (var job in protos.EnumeratePrototypes<Content.Shared.Roles.JobPrototype>())
            {
                if (job.RoundSide != Content.Shared.CMU14.Round.Roles.RoundJobSide.ThirdParty ||
                    job.RoundForce is not ("WY" or "WYPMC") ||
                    job.StartingGear is not { } gearId)
                {
                    continue;
                }

                var gear = protos.Index(gearId);
                Assert.That(gear.GetGear("ears"), Is.EqualTo("RMCHeadsetDistressWeYa"), $"{job.ID} ({gearId}) has the wrong headset.");
                checkedJobs++;
            }

            Assert.That(checkedJobs, Is.GreaterThan(20));
        });

        await pair.CleanReturnAsync();
    }
}

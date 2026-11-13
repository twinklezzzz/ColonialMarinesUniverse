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

    [TestCase("CMUGearWYSecurityTeamOfficer")]
    [TestCase("CMUGearWYSecurityTeamSergeant")]
    public async Task SecurityTeamKitIsArmed(string gear)
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

            Assert.That(Worn("jumpsuit"), Is.EqualTo("AU14WYSecurityUniformRig"));
            Assert.That(Worn("belt"), Is.EqualTo("RMCM77BeltPMCFilled"));
            Assert.That(Worn("suitstorage"), Is.EqualTo("RMCWeaponRifleSSG45"));

            Assert.That(inventory.TryGetSlotEntity(mob, "back", out var bag), Is.True);
            var stored = containers.GetContainer(bag!.Value, StorageComponent.ContainerId).ContainedEntities.Select(Proto).ToList();
            Assert.That(stored, Is.SupersetOf(new[] { "RMCWeaponTaser", "CMStunbaton", "RMCHandcuffs" }), "The satchel is missing the taser, baton or cuffs.");

            entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }
}

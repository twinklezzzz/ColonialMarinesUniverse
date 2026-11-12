using System.Linq;
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
            Assert.That(Stored(helmet!.Value), Is.SupersetOf(new[] { "RMCRC6RiotShield", "AU14HelmetGarbRiotVisor" }), "The helmet is missing its shield or visor.");

            Assert.That(inventory.TryGetSlotEntity(mob, "back", out var bag), Is.True);
            Assert.That(Stored(bag!.Value), Does.Contain("AU14TeleBaton"));

            Assert.That(inventory.TryGetSlotEntity(mob, "suitstorage", out var gun), Is.True);
            Assert.That(Proto(gun!.Value), Is.EqualTo(weapon));

            entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }
}

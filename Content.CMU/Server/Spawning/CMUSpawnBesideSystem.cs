using Content.Shared.CMU14.Spawning;

namespace Content.Server.CMU14.Spawning;

public sealed class CMUSpawnBesideSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CMUSpawnBesideComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<CMUSpawnBesideComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent);
        if (xform.MapUid == null)
            return;

        var coords = _transform.GetMoverCoordinates(ent, xform);
        foreach (var spawn in ent.Comp.Spawns)
        {
            Spawn(spawn, coords);
        }

        RemCompDeferred<CMUSpawnBesideComponent>(ent);
    }
}

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CMU14.Spawning;

/// <summary>
///     Spawns entities next to this one when it enters the map, for kit too big to carry,
///     like the Dog Catcher's specimen crate. Wherever a third party arrives, by shuttle or on foot,
///     the kit arrives with them, and no map needs a spot for it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CMUSpawnBesideComponent : Component
{
    [DataField(required: true)]
    public List<EntProtoId> Spawns = new();
}

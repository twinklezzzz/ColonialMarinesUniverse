using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.Specimens;

/// <summary>
///     A projectile that builds up sedation on the xenonid it hits. Enough hits in a short time
///     put the xenonid under for <see cref="SedationTime"/>; further hits keep it under.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(CMUSpecimenCaptureSystem))]
public sealed partial class CMUSedateOnHitComponent : Component
{
    [DataField]
    public float Amount = 1f;

    /// <summary>
    ///     Paralysis applied when the target goes under, before RMC's xeno stun reduction.
    /// </summary>
    [DataField]
    public TimeSpan SedationTime = TimeSpan.FromSeconds(30);
}

/// <summary>
///     Sedation built up on a xenonid. Decays over time.
/// </summary>
[RegisterComponent, Access(typeof(CMUSpecimenCaptureSystem))]
public sealed partial class CMUSedationComponent : Component
{
    [DataField]
    public float Level;

    [DataField]
    public TimeSpan LastUpdate;
}

/// <summary>
///     A xenonid bound in a specimen restraint harness. Kept paralysed until it tears free,
///     the harness is removed, or it is released from containment. The breakout timer
///     doesn't run while the specimen is contained.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(CMUSpecimenCaptureSystem))]
public sealed partial class CMUSpecimenRestrainedComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan BreakFreeAt;

    [DataField]
    public EntProtoId Harness = "CMUSpecimenHarness";

    [DataField]
    public TimeSpan NextParalyze;
}

[RegisterComponent, NetworkedComponent, Access(typeof(CMUSpecimenCaptureSystem))]
public sealed partial class CMUSpecimenHarnessComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How long an uncontained specimen stays bound before it tears free.
    /// </summary>
    [DataField]
    public TimeSpan BreakoutTime = TimeSpan.FromMinutes(3);
}

[Serializable, NetSerializable]
public enum CMUSpecimenCrateState : byte
{
    Closed,
    Opening,
    Open,
    Closing,
}

[Serializable, NetSerializable]
public enum CMUSpecimenCrateLayers : byte
{
    Base,
}

/// <summary>
///     A secure crate that holds one restrained specimen and can be pulled. It opens and closes
///     with an animation; a specimen can only be loaded or let out while it's open.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), Access(typeof(CMUSpecimenCaptureSystem))]
public sealed partial class CMUSpecimenCrateComponent : Component
{
    [DataField]
    public string ContainerId = "cmu_specimen";

    [DataField]
    public TimeSpan LoadDelay = TimeSpan.FromSeconds(4);

    [DataField, AutoNetworkedField]
    public CMUSpecimenCrateState State = CMUSpecimenCrateState.Open;

    /// <summary>
    ///     When the lid finishes moving, while opening or closing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? MovingUntil;

    /// <summary>
    ///     How long the lid takes, matching the sprite's four 0.365s frames.
    /// </summary>
    [DataField]
    public TimeSpan MoveTime = TimeSpan.FromSeconds(1.46);

    [DataField]
    public SoundSpecifier? MoveSound = new SoundPathSpecifier("/Audio/_RMC14/Structures/secure_box_opening/secure_box_opening.ogg");
}

[Serializable, NetSerializable]
public sealed partial class CMUSpecimenHarnessDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CMUSpecimenUnharnessDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CMUSpecimenLoadDoAfterEvent : SimpleDoAfterEvent;

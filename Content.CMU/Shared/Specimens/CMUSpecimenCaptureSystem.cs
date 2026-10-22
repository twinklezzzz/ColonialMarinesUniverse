using Content.Shared._RMC14.Pulling;
using Content.Shared._RMC14.Stun;
using Content.Shared._RMC14.Xenonids;
using Content.Shared.CMU14.Threats.Mobs.Biomorph;
using Content.Shared.CMU14.Threats.Mobs.Wendigo.Pack;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CMU14.Specimens;

/// <summary>
///     Live xenonid capture: capture slugs sedate, a restraint harness binds a downed xenonid,
///     and a secure crate holds a bound one so it can be hauled away.
///
///     Works on every hostile creature: xenonids and their variants (neomorphs, the abomination),
///     wendigos (natural and lab-made) and the flesh biomorphs. Biomorphs can't be knocked over, so they're held with
///     a plain stun instead of paralysis.
///
///     RMC cuts every stun on a xenonid to two thirds and won't re-stun one that's already down,
///     so a single long stun never holds. Sedation and restraint instead keep refreshing a short
///     hold for as long as they last.
/// </summary>
public sealed partial class CMUSpecimenCaptureSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;

    /// <summary>
    ///     Sedation lost per second, so hits have to land close together.
    /// </summary>
    public const float SedationDecay = 0.1f;

    private static readonly TimeSpan RestraintRefresh = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RestraintParalysis = TimeSpan.FromSeconds(3);

    public override void Initialize()
    {
        SubscribeLocalEvent<CMUSedateOnHitComponent, ProjectileHitEvent>(OnSedateHit);

        SubscribeLocalEvent<CMUSpecimenHarnessComponent, AfterInteractEvent>(OnHarnessAfterInteract);
        SubscribeLocalEvent<CMUSpecimenHarnessComponent, CMUSpecimenHarnessDoAfterEvent>(OnHarnessDoAfter);

        SubscribeLocalEvent<CMUSpecimenRestrainedComponent, ComponentStartup>(OnRestrainedStartup);
        SubscribeLocalEvent<CMUSpecimenRestrainedComponent, ComponentShutdown>(OnRestrainedShutdown);
        SubscribeLocalEvent<CMUSpecimenRestrainedComponent, UpdateCanMoveEvent>(OnRestrainedCanMove);
        SubscribeLocalEvent<CMUSpecimenRestrainedComponent, AttackAttemptEvent>(OnRestrainedAttack);
        SubscribeLocalEvent<CMUSpecimenRestrainedComponent, ExaminedEvent>(OnRestrainedExamined);
        SubscribeLocalEvent<CMUSpecimenRestrainedComponent, GetVerbsEvent<AlternativeVerb>>(OnRestrainedVerbs);
        SubscribeLocalEvent<CMUSpecimenRestrainedComponent, CMUSpecimenUnharnessDoAfterEvent>(OnUnharnessDoAfter);

        SubscribeLocalEvent<ActionComponent, ActionAttemptEvent>(OnActionAttempt);

        SubscribeLocalEvent<CMUSpecimenCrateComponent, ComponentInit>(OnCrateInit);
        SubscribeLocalEvent<CMUSpecimenCrateComponent, EntityTerminatingEvent>(OnCrateTerminating);
        SubscribeLocalEvent<CMUSpecimenCrateComponent, ExaminedEvent>(OnCrateExamined);
        SubscribeLocalEvent<CMUSpecimenCrateComponent, ActivateInWorldEvent>(OnCrateActivate);
        SubscribeLocalEvent<CMUSpecimenCrateComponent, GetVerbsEvent<AlternativeVerb>>(OnCrateVerbs);
        SubscribeLocalEvent<CMUSpecimenCrateComponent, CMUSpecimenLoadDoAfterEvent>(OnLoadDoAfter);
    }

    #region Sedation

    private void OnSedateHit(Entity<CMUSedateOnHitComponent> ent, ref ProjectileHitEvent args)
    {
        if (_net.IsClient)
            return;

        Sedate(args.Target, ent.Comp.Amount, ent.Comp.SedationTime);
    }

    /// <summary>
    ///     Adds sedation to a xenonid. Returns true if it is now sedated.
    /// </summary>
    public bool Sedate(EntityUid target, float amount, TimeSpan sedationTime)
    {
        if (!IsSpecimen(target))
            return false;

        var sedation = EnsureComp<CMUSedationComponent>(target);
        var now = _timing.CurTime;
        var elapsed = (float) (now - sedation.LastUpdate).TotalSeconds;
        sedation.Level = MathF.Max(0, sedation.Level - elapsed * SedationDecay);
        sedation.LastUpdate = now;

        var threshold = GetSedationThreshold(target);
        var wasSedated = sedation.Level >= threshold;
        sedation.Level = MathF.Min(threshold, sedation.Level + amount);
        if (sedation.Level < threshold)
        {
            _popup.PopupEntity(Loc.GetString("cmu-specimen-sedation-building", ("target", target)), target, PopupType.SmallCaution);
            return false;
        }

        Hold(target, sedationTime);
        if (!wasSedated)
            _popup.PopupEntity(Loc.GetString("cmu-specimen-sedated", ("target", target)), target, PopupType.MediumCaution);

        return true;
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is a creature the capture kit works on.
    /// </summary>
    public bool IsSpecimen(EntityUid uid)
    {
        return HasComp<XenoComponent>(uid) || HasComp<BiomorphComponent>(uid) || HasComp<CMUWendigoPackComponent>(uid);
    }

    /// <summary>
    ///     Down, or for creatures that can't fall over, stunned.
    /// </summary>
    public bool IsSubdued(EntityUid uid)
    {
        return _standing.IsDown(uid) || HasComp<StunnedComponent>(uid);
    }

    /// <summary>
    ///     Holds a specimen in place for <paramref name="time"/>. Paralysis where it can fall over,
    ///     a stun where it can't.
    /// </summary>
    private void Hold(EntityUid uid, TimeSpan time)
    {
        if (!_stun.TryParalyze(uid, time, true))
            _stun.TryUpdateStunDuration(uid, time);
    }

    /// <summary>
    ///     Hits needed in quick succession to put a xenonid under. Bigger castes take more.
    /// </summary>
    public float GetSedationThreshold(EntityUid target)
    {
        if (!TryComp<RMCSizeComponent>(target, out var size))
            return 3;

        return size.Size switch
        {
            <= RMCSizes.SmallXeno => 2,
            RMCSizes.Xeno => 3,
            _ => 5,
        };
    }

    #endregion

    #region Restraint

    private void OnHarnessAfterInteract(Entity<CMUSpecimenHarnessComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (!IsSpecimen(target))
            return;

        args.Handled = true;
        if (HasComp<CMUSpecimenRestrainedComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("cmu-specimen-already-restrained", ("target", target)), args.User, args.User);
            return;
        }

        if (!IsSubdued(target))
        {
            _popup.PopupEntity(Loc.GetString("cmu-specimen-must-be-down", ("target", target)), args.User, args.User);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay, new CMUSpecimenHarnessDoAfterEvent(), ent, target, ent)
        {
            BreakOnMove = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("cmu-specimen-harness-start", ("user", args.User), ("target", target)), target);
    }

    private void OnHarnessDoAfter(Entity<CMUSpecimenHarnessComponent> ent, ref CMUSpecimenHarnessDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;
        if (_net.IsClient || HasComp<CMUSpecimenRestrainedComponent>(target))
            return;

        Restrain(target, ent.Comp.BreakoutTime, MetaData(ent).EntityPrototype?.ID);
        _popup.PopupEntity(Loc.GetString("cmu-specimen-harnessed", ("user", args.User), ("target", target)), target);
        QueueDel(ent);
    }

    /// <summary>
    ///     Binds a specimen for <paramref name="breakoutTime"/> of uncontained time.
    /// </summary>
    public void Restrain(EntityUid target, TimeSpan breakoutTime, string? harness = null)
    {
        var restrained = EnsureComp<CMUSpecimenRestrainedComponent>(target);
        restrained.BreakFreeAt = _timing.CurTime + breakoutTime;
        if (harness != null)
            restrained.Harness = harness;

        Dirty(target, restrained);
        Hold(target, RestraintParalysis);
    }

    private void OnRestrainedStartup(Entity<CMUSpecimenRestrainedComponent> ent, ref ComponentStartup args)
    {
        _actionBlocker.UpdateCanMove(ent);

        // A live xenonid knocks down whoever grabs it. Bound, it can't, so handlers can drag it to a crate.
        if (_net.IsServer && TryComp<ParalyzeOnPullAttemptComponent>(ent, out var paralyze))
        {
            ent.Comp.SuspendedPullParalysis = paralyze.Duration;
            RemComp<ParalyzeOnPullAttemptComponent>(ent);
        }
    }

    private void OnRestrainedShutdown(Entity<CMUSpecimenRestrainedComponent> ent, ref ComponentShutdown args)
    {
        _actionBlocker.UpdateCanMove(ent);

        if (_net.IsServer &&
            ent.Comp.SuspendedPullParalysis is { } duration &&
            !TerminatingOrDeleted(ent))
        {
            var paralyze = EnsureComp<ParalyzeOnPullAttemptComponent>(ent);
            paralyze.Duration = duration;
            Dirty(ent, paralyze);
        }
    }

    private void OnRestrainedCanMove(Entity<CMUSpecimenRestrainedComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.LifeStage <= ComponentLifeStage.Running)
            args.Cancel();
    }

    private void OnRestrainedAttack(Entity<CMUSpecimenRestrainedComponent> ent, ref AttackAttemptEvent args)
    {
        args.Cancel();
    }

    /// <summary>
    ///     A bound or crated specimen can't use any of its abilities, acid included.
    /// </summary>
    private void OnActionAttempt(Entity<ActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (args.Cancelled || !IsHeld(args.User))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cmu-specimen-actions-blocked");
    }

    /// <summary>
    ///     Bound in a harness, or shut in a specimen crate.
    /// </summary>
    public bool IsHeld(EntityUid uid)
    {
        if (HasComp<CMUSpecimenRestrainedComponent>(uid))
            return true;

        return _container.TryGetContainingContainer(uid, out var container) &&
               HasComp<CMUSpecimenCrateComponent>(container.Owner);
    }

    private void OnRestrainedExamined(Entity<CMUSpecimenRestrainedComponent> ent, ref ExaminedEvent args)
    {
        if (_container.IsEntityInContainer(ent))
            return;

        var seconds = Math.Max(0, (int) (ent.Comp.BreakFreeAt - _timing.CurTime).TotalSeconds);
        args.PushMarkup(Loc.GetString("cmu-specimen-examine-restrained", ("target", ent.Owner), ("seconds", seconds)));
    }

    private void OnRestrainedVerbs(Entity<CMUSpecimenRestrainedComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.User == args.Target || IsSpecimen(args.User))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("cmu-specimen-verb-unharness"),
            Act = () =>
            {
                var doAfter = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(3), new CMUSpecimenUnharnessDoAfterEvent(), ent, ent)
                {
                    BreakOnMove = true,
                };
                _doAfter.TryStartDoAfter(doAfter);
            },
        });
    }

    private void OnUnharnessDoAfter(Entity<CMUSpecimenRestrainedComponent> ent, ref CMUSpecimenUnharnessDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (_net.IsClient)
            return;

        var harness = Spawn(ent.Comp.Harness, _transform.GetMapCoordinates(ent));
        _hands.PickupOrDrop(args.User, harness);
        RemComp<CMUSpecimenRestrainedComponent>(ent);
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        UpdateCrates();

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CMUSpecimenRestrainedComponent>();
        while (query.MoveNext(out var uid, out var restrained))
        {
            if (now < restrained.NextParalyze)
                continue;

            restrained.NextParalyze = now + RestraintRefresh;

            // A contained specimen can't work at the harness, so its breakout timer waits.
            if (_container.IsEntityInContainer(uid))
            {
                restrained.BreakFreeAt += RestraintRefresh;
                Dirty(uid, restrained);
            }
            else if (now >= restrained.BreakFreeAt)
            {
                _popup.PopupEntity(Loc.GetString("cmu-specimen-broke-free", ("target", uid)), uid, PopupType.LargeCaution);
                RemCompDeferred<CMUSpecimenRestrainedComponent>(uid);
                continue;
            }

            Hold(uid, RestraintParalysis);
        }
    }

    #endregion

    #region Containment

    private void OnCrateInit(Entity<CMUSpecimenCrateComponent> ent, ref ComponentInit args)
    {
        _container.EnsureContainer<ContainerSlot>(ent, ent.Comp.ContainerId);
    }

    private void OnCrateTerminating(Entity<CMUSpecimenCrateComponent> ent, ref EntityTerminatingEvent args)
    {
        // Never take the specimen with the crate.
        if (_container.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
            _container.EmptyContainer(container, true);
    }

    private void OnCrateExamined(Entity<CMUSpecimenCrateComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.State == CMUSpecimenCrateState.Closed ? "cmu-specimen-crate-examine-closed" : "cmu-specimen-crate-examine-open"));
        if (GetSpecimen(ent) is { } specimen)
            args.PushMarkup(Loc.GetString("cmu-specimen-crate-examine-full", ("target", specimen)));
        else
            args.PushMarkup(Loc.GetString("cmu-specimen-crate-examine-empty"));
    }

    private void OnCrateActivate(Entity<CMUSpecimenCrateComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || IsSpecimen(args.User))
            return;

        args.Handled = true;
        TryToggle(ent, args.User);
    }

    /// <summary>
    ///     Opens a closed crate or closes an open one. Does nothing while the lid is moving.
    /// </summary>
    public bool TryToggle(Entity<CMUSpecimenCrateComponent> ent, EntityUid? user = null)
    {
        return ent.Comp.State switch
        {
            CMUSpecimenCrateState.Open => StartMoving(ent, false, user),
            CMUSpecimenCrateState.Closed => StartMoving(ent, true, user),
            _ => false,
        };
    }

    private bool StartMoving(Entity<CMUSpecimenCrateComponent> ent, bool open, EntityUid? user = null)
    {
        ent.Comp.State = open ? CMUSpecimenCrateState.Opening : CMUSpecimenCrateState.Closing;
        ent.Comp.MovingUntil = _timing.CurTime + ent.Comp.MoveTime;
        Dirty(ent);
        _audio.PlayPredicted(ent.Comp.MoveSound, ent, user);
        return true;
    }

    private void UpdateCrates()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CMUSpecimenCrateComponent>();
        while (query.MoveNext(out var uid, out var crate))
        {
            if (crate.MovingUntil is not { } until || now < until)
                continue;

            crate.MovingUntil = null;
            crate.State = crate.State == CMUSpecimenCrateState.Opening ? CMUSpecimenCrateState.Open : CMUSpecimenCrateState.Closed;
            Dirty(uid, crate);
        }
    }

    private void OnCrateVerbs(Entity<CMUSpecimenCrateComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || IsSpecimen(args.User) || ent.Comp.State != CMUSpecimenCrateState.Open)
            return;

        var user = args.User;
        if (GetSpecimen(ent) != null)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("cmu-specimen-verb-release"),
                Act = () =>
                {
                    if (_net.IsClient || ent.Comp.State != CMUSpecimenCrateState.Open ||
                        !_container.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
                    {
                        return;
                    }

                    _container.EmptyContainer(container, true);
                },
            });
            return;
        }

        if (TryComp<PullerComponent>(user, out var puller) &&
            puller.Pulling is { } pulled &&
            HasComp<CMUSpecimenRestrainedComponent>(pulled))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("cmu-specimen-verb-load"),
                Act = () => _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.LoadDelay, new CMUSpecimenLoadDoAfterEvent(), ent, ent, pulled)
                {
                    BreakOnMove = true,
                }),
            });
        }
    }

    private void OnLoadDoAfter(Entity<CMUSpecimenCrateComponent> ent, ref CMUSpecimenLoadDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } specimen)
            return;

        args.Handled = true;
        if (_net.IsClient || ent.Comp.State != CMUSpecimenCrateState.Open || !HasComp<CMUSpecimenRestrainedComponent>(specimen))
            return;

        TryContain(ent, specimen);
    }

    public bool TryContain(Entity<CMUSpecimenCrateComponent> crate, EntityUid specimen)
    {
        if (GetSpecimen(crate) != null || !_container.TryGetContainer(crate, crate.Comp.ContainerId, out var container))
            return false;

        if (TryComp<PullableComponent>(specimen, out var pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(specimen, pullable);

        if (!_container.Insert(specimen, container))
            return false;

        _popup.PopupEntity(Loc.GetString("cmu-specimen-contained", ("target", specimen)), crate);
        return true;
    }

    public EntityUid? GetSpecimen(Entity<CMUSpecimenCrateComponent> crate)
    {
        if (!_container.TryGetContainer(crate, crate.Comp.ContainerId, out var container) || container.ContainedEntities.Count == 0)
            return null;

        return container.ContainedEntities[0];
    }

    #endregion
}

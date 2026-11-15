// SPDX-FileCopyrightText: 2026 Project Dumont
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Shared.SurveillanceCamera;
using Content.Goobstation.Shared.Xenobiology;
using Content.Goobstation.Shared.Xenobiology.Components;
using Content.Goobstation.Server.Xenobiology.SlimeGrinder;
using Content.Server.Chat.Managers;
using Content.Server.DeviceLinking.Systems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Power.Components;
using Content.Server.SurveillanceCamera;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Eye;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Power;
using Content.Shared.Popups;
using Content.Shared.Pulling.Events;
using Content.Shared.Tag;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Goobstation.Server.Xenobiology;

public sealed partial class XenobiologyConsoleSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private DeviceLinkSystem _deviceLink = default!;
    [Dependency] private SharedEyeSystem _eye = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SlimeScannerSystem _slimeScanner = default!;
    [Dependency] private SlimeGrinderSystem _slimeGrinder = default!;
    [Dependency] private SurveillanceCameraSystem _surveillanceCamera = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedConsoleCameraSystem _cameraVision = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenobiologyConsoleComponent, ComponentStartup>(OnConsoleStartup);
        SubscribeLocalEvent<XenobiologyConsoleComponent, ComponentShutdown>(OnConsoleShutdown);
        SubscribeLocalEvent<XenobiologyConsoleComponent, ActivateInWorldEvent>(OnConsoleActivated);
        SubscribeLocalEvent<XenobiologyConsoleComponent, AfterInteractUsingEvent>(OnConsoleAfterInteractUsing);
        SubscribeLocalEvent<XenobiologyConsoleComponent, PowerChangedEvent>(OnConsolePowerChanged);
        SubscribeLocalEvent<XenobiologyConsoleComponent, EntRemovedFromContainerMessage>(OnConsoleContainerRemoved);
        SubscribeLocalEvent<XenobiologyConsoleControllerComponent, ComponentShutdown>(OnControllerShutdown);
        SubscribeLocalEvent<XenobiologyConsoleControllerComponent, MoveEvent>(OnControllerMoved);
        SubscribeLocalEvent<XenobiologyConsoleControllerComponent, EntParentChangedMessage>(OnControllerParentChanged);
        SubscribeLocalEvent<XenobiologyConsoleControllerComponent, PullStartedMessage>(OnControllerPullStarted);
        SubscribeLocalEvent<XenobiologyConsoleControllerComponent, BeingPulledAttemptEvent>(OnControllerBeingPulledAttempt);
        SubscribeLocalEvent<XenobiologyConsoleRemoteComponent, MoveEvent>(OnRemoteMoved);

        SubscribeLocalEvent<XenobiologyConsoleStoredSlimeComponent, UpdateCanMoveEvent>(OnStoredSlimeMoveAttempt);
        SubscribeLocalEvent<XenobiologyConsoleStoredSlimeComponent, AttackAttemptEvent>(OnStoredSlimeAttackAttempt);
        SubscribeLocalEvent<XenobiologyConsoleStoredSlimeComponent, UseAttemptEvent>(OnStoredSlimeUseAttempt);
        SubscribeLocalEvent<XenobiologyConsoleStoredSlimeComponent, StartPullAttemptEvent>(OnStoredSlimePullAttempt);
        SubscribeLocalEvent<XenobiologyConsoleStoredSlimeComponent, ComponentShutdown>(OnStoredSlimeShutdown);

        SubscribeLocalEvent<XenobiologyConsoleExitEvent>(OnExit);
        SubscribeLocalEvent<XenobiologyConsolePlaceMonkeyEvent>(OnPlaceMonkey);
        SubscribeLocalEvent<XenobiologyConsoleRecycleMonkeyEvent>(OnRecycleMonkey);
        SubscribeLocalEvent<XenobiologyConsoleGrabSlimeEvent>(OnGrabSlime);
        SubscribeLocalEvent<XenobiologyConsoleReleaseSlimesEvent>(OnReleaseSlimes);
        SubscribeLocalEvent<XenobiologyConsoleAnalyzeSlimeEvent>(OnAnalyzeSlime);
        SubscribeLocalEvent<XenobiologyConsoleShowShortcutsEvent>(OnShowShortcuts);

        SubscribeNetworkEvent<XenobiologyConsoleShortcutRequest>(OnShortcutRequest);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<XenobiologyConsoleControllerComponent>();
        while (query.MoveNext(out var uid, out var controller))
        {
            if (_gameTiming.CurTime < controller.NextValidationTime)
                continue;

            controller.NextValidationTime = _gameTiming.CurTime + GetValidationInterval(controller);

            if (!IsSessionValid(uid, controller))
            {
                StopControl((uid, controller), removeController: true);
                continue;
            }

            ValidateCameraCoverage((uid, controller));
        }
    }

    private void OnConsoleStartup(Entity<XenobiologyConsoleComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.SlimeContainer = _container.EnsureContainer<Container>(ent, XenobiologyConsoleComponent.SlimeContainerId);
    }

    private void OnConsoleShutdown(Entity<XenobiologyConsoleComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ActiveUser is { } user && TryComp<XenobiologyConsoleControllerComponent>(user, out var controller))
            StopControl((user, controller), removeController: true);

        ReleaseSlimes(ent, Transform(ent).Coordinates);
        QueueDel(ent.Comp.RemoteEntity);
    }

    private void OnConsoleActivated(Entity<XenobiologyConsoleComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        if (!HasConsoleAccess(args.User, ent))
        {
            _popup.PopupEntity(Loc.GetString("xenobiology-console-no-access"), ent, args.User);
            args.Handled = true;
            return;
        }

        if (!IsConsolePowered(ent))
            return;

        if (ent.Comp.ActiveUser is { } active && active != args.User && !Terminating(active))
        {
            _popup.PopupEntity(Loc.GetString("xenobiology-console-in-use"), ent, args.User);
            args.Handled = true;
            return;
        }

        if (ent.Comp.ActiveUser == args.User && TryComp<XenobiologyConsoleControllerComponent>(args.User, out var activeController))
        {
            StopControl((args.User, activeController), removeController: true);
            args.Handled = true;
            return;
        }

        StartControl(args.User, ent);
        args.Handled = true;
    }

    private void OnConsoleAfterInteractUsing(Entity<XenobiologyConsoleComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || !_tag.HasTag(args.Used, ent.Comp.MonkeyCubeTag))
            return;

        if (!HasConsoleAccess(args.User, ent))
        {
            _popup.PopupEntity(Loc.GetString("xenobiology-console-no-access"), ent, args.User);
            args.Handled = true;
            return;
        }

        ent.Comp.MonkeyBiomass += ent.Comp.MonkeyCubeBiomass;
        QueueDel(args.Used);
        UpdateViewState(ent);
        _popup.PopupEntity(Loc.GetString("xenobiology-console-cube-inserted", ("amount", ent.Comp.MonkeyBiomass)), ent, args.User);
        args.Handled = true;
    }

    private void OnConsolePowerChanged(Entity<XenobiologyConsoleComponent> ent, ref PowerChangedEvent args)
    {
        if (args.Powered ||
            ent.Comp.ActiveUser is not { } user ||
            !TryComp<XenobiologyConsoleControllerComponent>(user, out var controller))
        {
            return;
        }

        StopControl((user, controller), removeController: true);
    }

    private void OnConsoleContainerRemoved(Entity<XenobiologyConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != XenobiologyConsoleComponent.SlimeContainerId)
            return;

        RestoreStoredSlime(args.Entity);
        UpdateViewState(ent);
    }
}

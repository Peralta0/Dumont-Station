// SPDX-FileCopyrightText: 2026 Project Dumont
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Shared.Xenobiology.Components;
using Content.Server.Power.Components;
using Content.Shared.Access.Components;
using Content.Shared.Eye;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Pulling.Events;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Goobstation.Server.Xenobiology;

public sealed partial class XenobiologyConsoleSystem
{
    private void OnControllerShutdown(Entity<XenobiologyConsoleControllerComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.CleaningUp)
            return;

        StopControl(ent, removeController: false);
    }

    private void OnControllerMoved(Entity<XenobiologyConsoleControllerComponent> ent, ref MoveEvent args)
    {
        StopControlIfSessionInvalid(ent);
    }

    private void OnControllerParentChanged(Entity<XenobiologyConsoleControllerComponent> ent, ref EntParentChangedMessage args)
    {
        StopControlIfSessionInvalid(ent);
    }

    private void OnControllerPullStarted(Entity<XenobiologyConsoleControllerComponent> ent, ref PullStartedMessage args)
    {
        StopControl(ent, removeController: true);
    }

    private void OnControllerBeingPulledAttempt(Entity<XenobiologyConsoleControllerComponent> ent, ref BeingPulledAttemptEvent args)
    {
        StopControl(ent, removeController: true);
    }

    private void StartControl(EntityUid user, Entity<XenobiologyConsoleComponent> console)
    {
        if (TryComp<XenobiologyConsoleControllerComponent>(user, out var oldController))
        {
            StopControl((user, oldController), removeController: true);
            return;
        }

        if (!TryFindInitialCamera(console, out var camera))
        {
            _popup.PopupEntity(Loc.GetString("xenobiology-console-no-cameras"), console, user);
            return;
        }

        if (console.Comp.SlimeContainer is null ||
            console.Comp.SlimeContainer.Owner == EntityUid.Invalid)
            console.Comp.SlimeContainer = _container.EnsureContainer<Container>(console, XenobiologyConsoleComponent.SlimeContainerId);

        var remote = Spawn(console.Comp.RemoteEntityPrototype, Transform(camera).Coordinates);
        var remoteComp = EnsureComp<XenobiologyConsoleRemoteComponent>(remote);
        remoteComp.Controller = user;

        var controller = EnsureComp<XenobiologyConsoleControllerComponent>(user);
        controller.Console = console;
        controller.RemoteEntity = remote;
        controller.ActiveCamera = camera;
        controller.LastValidCoordinates = Transform(remote).Coordinates;
        controller.NextValidationTime = _gameTiming.CurTime + console.Comp.SessionValidationInterval;
        _actions.AddAction(user, ref controller.ExitActionEntity, controller.ExitAction);
        _actions.AddAction(user, ref controller.PlaceMonkeyActionEntity, controller.PlaceMonkeyAction);
        _actions.AddAction(user, ref controller.RecycleMonkeyActionEntity, controller.RecycleMonkeyAction);
        _actions.AddAction(user, ref controller.GrabSlimeActionEntity, controller.GrabSlimeAction);
        _actions.AddAction(user, ref controller.ReleaseSlimesActionEntity, controller.ReleaseSlimesAction);
        _actions.AddAction(user, ref controller.AnalyzeSlimeActionEntity, controller.AnalyzeSlimeAction);
        _actions.AddAction(user, ref controller.ShowShortcutsActionEntity, controller.ShowShortcutsAction);

        if (TryComp(user, out EyeComponent? eyeComp))
        {
            controller.PreviousEyeTarget = eyeComp.Target;
            controller.PreviousDrawFov = eyeComp.DrawFov;
            controller.PreviousDrawLight = eyeComp.DrawLight;
            _eye.SetTarget(user, remote, eyeComp);
            _eye.SetDrawFov(user, false, eyeComp);
            _eye.SetDrawLight((user, eyeComp), true);
        }

        var view = EnsureComp<XenobiologyConsoleViewComponent>(user);
        view.RequiredCameraTag = console.Comp.RequiredCameraTag;
        view.CameraOverlaySearchRange = console.Comp.CameraOverlaySearchRange;
        UpdateViewState(console, (user, view));

        AddCameraViewer(camera, user, console);

        if (TryComp<RelayInputMoverComponent>(user, out var relay))
        {
            var relayEntity = relay.RelayEntity;
            if (relayEntity.IsValid() && !TerminatingOrDeleted(relayEntity))
                controller.PreviousRelayEntity = relayEntity;
        }

        _mover.SetRelay(user, remote);

        console.Comp.ActiveUser = user;
        console.Comp.RemoteEntity = remote;

        _popup.PopupEntity(Loc.GetString("xenobiology-console-connected"), console, user);
    }

    private void StopControl(Entity<XenobiologyConsoleControllerComponent> controller, bool removeController)
    {
        if (controller.Comp.CleaningUp)
            return;

        controller.Comp.CleaningUp = true;

        if (controller.Comp.Console is { } consoleUid && TryComp<XenobiologyConsoleComponent>(consoleUid, out var console))
        {
            if (console.ActiveUser == controller)
                console.ActiveUser = null;

            if (console.RemoteEntity == controller.Comp.RemoteEntity)
                console.RemoteEntity = null;

        }

        _actions.RemoveAction(controller.Comp.ExitActionEntity);
        _actions.RemoveAction(controller.Comp.PlaceMonkeyActionEntity);
        _actions.RemoveAction(controller.Comp.RecycleMonkeyActionEntity);
        _actions.RemoveAction(controller.Comp.GrabSlimeActionEntity);
        _actions.RemoveAction(controller.Comp.ReleaseSlimesActionEntity);
        _actions.RemoveAction(controller.Comp.AnalyzeSlimeActionEntity);
        _actions.RemoveAction(controller.Comp.ShowShortcutsActionEntity);

        RemComp<XenobiologyConsoleViewComponent>(controller);

        if (controller.Comp.ActiveCamera is { } camera)
            RemoveCameraViewer(camera, controller, controller.Comp.Console);

        if (TryComp(controller, out EyeComponent? eyeComp))
        {
            var previousTarget = controller.Comp.PreviousEyeTarget;
            if (previousTarget is { } target && TerminatingOrDeleted(target))
                previousTarget = null;

            _eye.SetTarget(controller, previousTarget, eyeComp);
            _eye.SetDrawFov(controller, controller.Comp.PreviousDrawFov ?? true, eyeComp);
            _eye.SetDrawLight((controller, eyeComp), controller.Comp.PreviousDrawLight ?? true);
        }

        if (controller.Comp.PreviousRelayEntity is { } previousRelay &&
            !TerminatingOrDeleted(previousRelay))
        {
            _mover.SetRelay(controller, previousRelay);
        }
        else
        {
            RemComp<RelayInputMoverComponent>(controller);
        }

        QueueDel(controller.Comp.RemoteEntity);

        if (removeController && !Terminating(controller))
            RemCompDeferred<XenobiologyConsoleControllerComponent>(controller);
    }

    private bool TryGetSession(EntityUid user, out Entity<XenobiologyConsoleComponent> console, out EntityUid remote)
    {
        console = default;
        remote = default;

        if (!TryComp<XenobiologyConsoleControllerComponent>(user, out var controller) ||
            controller.Console is not { } consoleUid ||
            controller.RemoteEntity is not { } remoteUid ||
            !TryComp<XenobiologyConsoleComponent>(consoleUid, out var consoleComp) ||
            Terminating(consoleUid) ||
            Terminating(remoteUid))
        {
            return false;
        }

        console = (consoleUid, consoleComp);
        remote = remoteUid;

        if (!IsSessionValid(user, controller))
        {
            StopControl((user, controller), removeController: true);
            console = default;
            remote = default;
            return false;
        }

        if (!TryRefreshCameraCoverage((user, controller), console, Transform(remote).Coordinates))
        {
            StopControl((user, controller), removeController: true);
            console = default;
            remote = default;
            return false;
        }

        return true;
    }

    private bool IsSessionValid(EntityUid user, XenobiologyConsoleControllerComponent controller)
    {
        if (controller.Console is not { } consoleUid ||
            controller.RemoteEntity is not { } remoteUid ||
            !TryComp<XenobiologyConsoleComponent>(consoleUid, out var console) ||
            !TryComp<XenobiologyConsoleRemoteComponent>(remoteUid, out var remote) ||
            Terminating(consoleUid) ||
            Terminating(remoteUid) ||
            Terminating(user) ||
            console.ActiveUser != user ||
            console.RemoteEntity != remoteUid ||
            remote.Controller != user ||
            !HasConsoleAccess(user, consoleUid) ||
            !IsConsolePowered((consoleUid, console)) ||
            !IsUserInRange(user, controller, (consoleUid, console)))
        {
            return false;
        }

        return true;
    }

    private bool IsConsolePowered(Entity<XenobiologyConsoleComponent> console)
    {
        return !TryComp<ApcPowerReceiverComponent>(console, out var power) || power.Powered;
    }

    private bool HasConsoleAccess(EntityUid user, EntityUid console)
    {
        return !TryComp<AccessReaderComponent>(console, out var access) ||
               _access.IsAllowed(user, console, access);
    }

    private bool IsUserInRange(
        EntityUid user,
        XenobiologyConsoleControllerComponent controller,
        Entity<XenobiologyConsoleComponent> console)
    {
        var origin = user;
        if (controller.PreviousRelayEntity is { } relay && !TerminatingOrDeleted(relay))
            origin = relay;

        return Transform(origin).Coordinates.TryDistance(EntityManager, Transform(console).Coordinates, out var distance) &&
               distance <= console.Comp.UserMaxDistance;
    }

    private TimeSpan GetValidationInterval(XenobiologyConsoleControllerComponent controller)
    {
        if (controller.Console is { } consoleUid &&
            TryComp<XenobiologyConsoleComponent>(consoleUid, out var console))
        {
            return console.SessionValidationInterval;
        }

        return TimeSpan.FromSeconds(0.5);
    }

    private void StopControlIfSessionInvalid(Entity<XenobiologyConsoleControllerComponent> controller)
    {
        if (TerminatingOrDeleted(controller))
            return;

        if (!IsSessionValid(controller, controller.Comp))
            StopControl(controller, removeController: true);
    }
}

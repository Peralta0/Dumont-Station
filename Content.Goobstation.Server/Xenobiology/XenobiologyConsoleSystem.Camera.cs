// SPDX-FileCopyrightText: 2026 Project Dumont
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Shared.SurveillanceCamera;
using Content.Server.SurveillanceCamera;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Goobstation.Server.Xenobiology;

public sealed partial class XenobiologyConsoleSystem
{
    private void OnRemoteMoved(Entity<XenobiologyConsoleRemoteComponent> ent, ref MoveEvent args)
    {
        if (ent.Comp.ReturningToCameraView ||
            TerminatingOrDeleted(ent) ||
            ent.Comp.Controller is not { } user ||
            !TryComp<XenobiologyConsoleControllerComponent>(user, out var controller) ||
            controller.Console is not { } consoleUid ||
            !TryComp<XenobiologyConsoleComponent>(consoleUid, out var console))
        {
            return;
        }

        var controllerEnt = new Entity<XenobiologyConsoleControllerComponent>(user, controller);
        var consoleEnt = new Entity<XenobiologyConsoleComponent>(consoleUid, console);

        ValidateCameraCoverage(controllerEnt, consoleEnt, ent, args.OldPosition);
    }

    private void ValidateCameraCoverage(Entity<XenobiologyConsoleControllerComponent> controller)
    {
        if (controller.Comp.RemoteEntity is not { } remote ||
            controller.Comp.Console is not { } consoleUid ||
            !TryComp<XenobiologyConsoleComponent>(consoleUid, out var console))
        {
            return;
        }

        ValidateCameraCoverage(controller, (consoleUid, console), remote);
    }

    private void ValidateCameraCoverage(
        Entity<XenobiologyConsoleControllerComponent> controller,
        Entity<XenobiologyConsoleComponent> console,
        EntityUid remote,
        EntityCoordinates? previousCoordinates = null)
    {
        if (TryRefreshCameraCoverage(controller, console, Transform(remote).Coordinates))
            return;

        EntityCoordinates? fallback = null;
        if (controller.Comp.LastValidCoordinates is { } lastValid &&
            TryFindVisibleCamera(lastValid, console.Comp, out _))
        {
            fallback = lastValid;
        }
        else if (previousCoordinates is { } previous &&
                 TryFindVisibleCamera(previous, console.Comp, out _))
        {
            fallback = previous;
        }

        if (fallback is not { } visibleFallback ||
            !TryComp<XenobiologyConsoleRemoteComponent>(remote, out var remoteComp))
        {
            StopControl(controller, removeController: true);
            return;
        }

        remoteComp.ReturningToCameraView = true;
        try
        {
            _transform.SetCoordinates(remote, visibleFallback);
        }
        finally
        {
            remoteComp.ReturningToCameraView = false;
        }

        if (!TryRefreshCameraCoverage(controller, console, visibleFallback))
            StopControl(controller, removeController: true);
    }

    private bool TryRefreshCameraCoverage(
        Entity<XenobiologyConsoleControllerComponent> controller,
        Entity<XenobiologyConsoleComponent> console,
        EntityCoordinates coordinates)
    {
        if (!coordinates.IsValid(EntityManager))
            return false;

        if (controller.Comp.ActiveCamera is { } activeCamera &&
            IsVisibleFromCamera(activeCamera, console.Comp, coordinates))
        {
            controller.Comp.LastValidCoordinates = coordinates;
            return true;
        }

        if (!TryFindVisibleCamera(coordinates, console.Comp, out var camera))
            return false;

        controller.Comp.LastValidCoordinates = coordinates;
        SetActiveCamera(controller, console, camera);
        return true;
    }

    private void SetActiveCamera(
        Entity<XenobiologyConsoleControllerComponent> controller,
        Entity<XenobiologyConsoleComponent> console,
        EntityUid camera)
    {
        if (controller.Comp.ActiveCamera == camera)
            return;

        if (controller.Comp.ActiveCamera is { } oldCamera)
            RemoveCameraViewer(oldCamera, controller, console);

        AddCameraViewer(camera, controller, console);
        controller.Comp.ActiveCamera = camera;
    }

    private void AddCameraViewer(EntityUid camera, EntityUid user, EntityUid? monitor)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        _surveillanceCamera.AddActiveViewer(camera, user, monitor, actor: actor);
    }

    private void RemoveCameraViewer(EntityUid camera, EntityUid user, EntityUid? monitor)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        _surveillanceCamera.RemoveActiveViewer(camera, user, monitor, actor: actor);
    }

    private bool TryFindInitialCamera(Entity<XenobiologyConsoleComponent> console, out EntityUid camera)
    {
        return TryFindVisibleCamera(console, console.Comp, out camera) ||
               TryFindClosestCamera(console, console.Comp, out camera);
    }

    private bool TryFindVisibleCamera(EntityUid target, XenobiologyConsoleComponent console, out EntityUid camera)
    {
        return TryFindVisibleCamera(Transform(target).Coordinates, console, out camera);
    }

    private bool TryFindVisibleCamera(EntityCoordinates targetCoords, XenobiologyConsoleComponent console, out EntityUid camera)
    {
        var closestDistance = float.MaxValue;
        camera = default;

        foreach (var candidate in FindAvailableCameras(targetCoords, console))
        {
            if (!IsVisibleFromCamera(candidate, console, targetCoords))
                continue;

            if (!targetCoords.TryDistance(EntityManager, Transform(candidate).Coordinates, out var distance) ||
                distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            camera = candidate;
        }

        return closestDistance < float.MaxValue;
    }

    private bool IsVisibleFromCamera(EntityUid camera, XenobiologyConsoleComponent console, EntityCoordinates targetCoords)
    {
        if (!targetCoords.IsValid(EntityManager) ||
            !TryComp<ConsoleCameraComponent>(camera, out var consoleCamera) ||
            !consoleCamera.Tags.Contains(console.RequiredCameraTag) ||
            !TryComp<SurveillanceCameraComponent>(camera, out var surveillanceCamera) ||
            !surveillanceCamera.Active ||
            Transform(camera).MapID != _transform.ToMapCoordinates(targetCoords).MapId)
        {
            return false;
        }

        return IsInCameraView(camera, consoleCamera, targetCoords);
    }

    private bool TryFindClosestCamera(EntityUid target, XenobiologyConsoleComponent console, out EntityUid camera)
    {
        var targetCoords = Transform(target).Coordinates;
        var closestDistance = float.MaxValue;
        camera = default;

        foreach (var candidate in FindAvailableCameras(targetCoords, console))
        {
            if (!targetCoords.TryDistance(EntityManager, Transform(candidate).Coordinates, out var distance) ||
                distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            camera = candidate;
        }

        return closestDistance < float.MaxValue;
    }

    private IEnumerable<Entity<ConsoleCameraComponent>> FindAvailableCameras(
        EntityCoordinates targetCoords,
        XenobiologyConsoleComponent console)
    {
        var targetMap = _transform.ToMapCoordinates(targetCoords).MapId;
        foreach (var candidate in _lookup.GetEntitiesInRange<ConsoleCameraComponent>(
                     targetCoords,
                     console.CameraSearchRange,
                     LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Approximate))
        {
            if (!candidate.Comp.Tags.Contains(console.RequiredCameraTag) ||
                !TryComp<SurveillanceCameraComponent>(candidate, out var surveillanceCamera) ||
                !surveillanceCamera.Active ||
                Transform(candidate).MapID != targetMap)
            {
                continue;
            }

            yield return candidate;
        }
    }

    private bool IsInCameraView(EntityUid camera, ConsoleCameraComponent consoleCamera, EntityCoordinates targetCoords)
    {
        return _cameraVision.TryCreateVision(camera, consoleCamera, out var vision) &&
               _cameraVision.IsVisible(vision, targetCoords);
    }
}

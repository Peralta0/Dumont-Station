// SPDX-FileCopyrightText: 2026 Project Dumont
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Server.Xenobiology.SlimeGrinder;
using Content.Goobstation.Shared.Xenobiology;
using Content.Goobstation.Shared.Xenobiology.Components;
using Content.Shared.DeviceLinking;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Goobstation.Server.Xenobiology;

public sealed partial class XenobiologyConsoleSystem
{
    private void OnExit(XenobiologyConsoleExitEvent ev)
    {
        if (!TryComp<XenobiologyConsoleControllerComponent>(ev.Performer, out var controller))
            return;

        StopControl((ev.Performer, controller), removeController: true);
        ev.Handled = true;
    }

    private void OnPlaceMonkey(XenobiologyConsolePlaceMonkeyEvent ev)
    {
        if (!TryGetSession(ev.Performer, out var console, out var remote))
            return;

        TryPlaceMonkey(ev.Performer, console, Transform(remote).Coordinates);
        ev.Handled = true;
    }

    private void OnRecycleMonkey(XenobiologyConsoleRecycleMonkeyEvent ev)
    {
        if (!TryGetSession(ev.Performer, out var console, out var remote))
            return;

        if (!TryFindMonkey(remote, console.Comp, out var monkey))
        {
            _popup.PopupEntity(Loc.GetString("xenobiology-console-no-monkey"), ev.Performer, ev.Performer);
            ev.Handled = true;
            return;
        }

        TryRecycleMonkey(ev.Performer, console, monkey);
        ev.Handled = true;
    }

    private void OnGrabSlime(XenobiologyConsoleGrabSlimeEvent ev)
    {
        if (!TryGetSession(ev.Performer, out var console, out var remote))
            return;

        if (!TryFindSlime(remote, console.Comp, _ => true, out var slime))
        {
            _popup.PopupEntity(Loc.GetString("xenobiology-console-no-slime"), ev.Performer, ev.Performer);
            ev.Handled = true;
            return;
        }

        TryGrabSlime(ev.Performer, console, slime);
        ev.Handled = true;
    }

    private void OnReleaseSlimes(XenobiologyConsoleReleaseSlimesEvent ev)
    {
        if (!TryGetSession(ev.Performer, out var console, out var remote))
            return;

        TryReleaseSlimes(ev.Performer, console, Transform(remote).Coordinates);
        ev.Handled = true;
    }

    private void OnAnalyzeSlime(XenobiologyConsoleAnalyzeSlimeEvent ev)
    {
        if (!TryGetSession(ev.Performer, out var console, out var remote))
            return;

        if (!TryFindSlime(remote, console.Comp, _ => true, out var slime))
        {
            _popup.PopupEntity(Loc.GetString("xenobiology-console-no-slime"), ev.Performer, ev.Performer);
            ev.Handled = true;
            return;
        }

        AnalyzeSlime(ev.Performer, slime);
        ev.Handled = true;
    }

    private void OnShowShortcuts(XenobiologyConsoleShowShortcutsEvent ev)
    {
        if (!TryGetSession(ev.Performer, out _, out _) ||
            !TryComp<ActorComponent>(ev.Performer, out var actor))
        {
            return;
        }

        _chat.DispatchServerMessage(actor.PlayerSession, Loc.GetString("xenobiology-console-shortcuts-chat"));
        ev.Handled = true;
    }

    private void OnShortcutRequest(XenobiologyConsoleShortcutRequest request, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user ||
            !TryComp<XenobiologyConsoleControllerComponent>(user, out var controller) ||
            controller.Console is not { } consoleUid ||
            !TryComp<XenobiologyConsoleComponent>(consoleUid, out var consoleComp) ||
            _gameTiming.CurTime < controller.NextShortcutTime)
        {
            return;
        }

        controller.NextShortcutTime = _gameTiming.CurTime + consoleComp.ShortcutCooldown;

        if (!TryGetSession(user, out var console, out var remote))
            return;

        if (!TryGetEntity(request.Coordinates.NetEntity, out var coordinateEntity))
            return;

        var coordinates = new EntityCoordinates(coordinateEntity.Value, request.Coordinates.Position);
        if (!IsValidShortcutCoordinates(remote, console.Comp, coordinates))
            return;

        EntityUid? target = null;
        if (request.Target is { } netTarget &&
            TryGetEntity(netTarget, out var entity) &&
            !TerminatingOrDeleted(entity.Value))
        {
            target = entity.Value;
        }

        switch (request.Shortcut)
        {
            case XenobiologyConsoleShortcut.ShiftClick:
                HandleShiftClick(user, console, remote, target, coordinates);
                break;
            case XenobiologyConsoleShortcut.ControlClick:
                HandleControlClick(user, console, remote, target, coordinates);
                break;
        }
    }

    private void HandleShiftClick(
        EntityUid user,
        Entity<XenobiologyConsoleComponent> console,
        EntityUid remote,
        EntityUid? target,
        EntityCoordinates coordinates)
    {
        if (target is { } slimeUid &&
            TryComp<SlimeComponent>(slimeUid, out var slime) &&
            IsValidSlimeTarget(remote, console.Comp, slimeUid))
        {
            TryGrabSlime(user, console, (slimeUid, slime));
            return;
        }

        if (IsFloorTarget(target, coordinates))
            TryReleaseSlimes(user, console, coordinates);
    }

    private void HandleControlClick(
        EntityUid user,
        Entity<XenobiologyConsoleComponent> console,
        EntityUid remote,
        EntityUid? target,
        EntityCoordinates coordinates)
    {
        if (target is { } slimeUid &&
            TryComp<SlimeComponent>(slimeUid, out var slime) &&
            IsValidSlimeTarget(remote, console.Comp, slimeUid))
        {
            AnalyzeSlime(user, (slimeUid, slime));
            return;
        }

        if (target is { } monkey &&
            IsValidMonkeyTarget(remote, console.Comp, monkey))
        {
            TryRecycleMonkey(user, console, monkey);
            return;
        }

        if (IsFloorTarget(target, coordinates))
            TryPlaceMonkey(user, console, coordinates);
    }

    private bool TryPlaceMonkey(
        EntityUid user,
        Entity<XenobiologyConsoleComponent> console,
        EntityCoordinates coordinates)
    {
        if (console.Comp.MonkeyBiomass < console.Comp.MonkeySpawnCost)
        {
            _popup.PopupEntity(Loc.GetString("xenobiology-console-no-biomass"), user, user);
            return false;
        }

        var monkey = Spawn(console.Comp.MonkeyPrototype, coordinates);
        console.Comp.MonkeyBiomass -= console.Comp.MonkeySpawnCost;
        UpdateViewState(console);
        PlayTransferAnimation(console, coordinates, [monkey], XenobiologyTransferAnimationType.Release);
        _popup.PopupEntity(
            Loc.GetString("xenobiology-console-monkey-placed", ("amount", console.Comp.MonkeyBiomass)),
            user,
            user);
        return true;
    }

    private bool TryRecycleMonkey(
        EntityUid user,
        Entity<XenobiologyConsoleComponent> console,
        EntityUid monkey)
    {
        if (!_mobState.IsDead(monkey))
        {
            _popup.PopupEntity(Loc.GetString("xenobiology-console-monkey-alive"), user, user);
            return false;
        }

        var coordinates = Transform(monkey).Coordinates;
        PlayTransferAnimation(console, coordinates, [monkey], XenobiologyTransferAnimationType.Suction);
        QueueDel(monkey);
        console.Comp.MonkeyBiomass += console.Comp.MonkeyRecycleYield;
        UpdateViewState(console);
        _popup.PopupEntity(
            Loc.GetString("xenobiology-console-monkey-recycled", ("amount", console.Comp.MonkeyBiomass)),
            user,
            user);
        return true;
    }

    private bool TryGrabSlime(
        EntityUid user,
        Entity<XenobiologyConsoleComponent> console,
        Entity<SlimeComponent> slime)
    {
        var coordinates = Transform(slime).Coordinates;
        if (_mobState.IsDead(slime) && TrySendToLinkedGrinder(console, slime))
        {
            PlayTransferAnimation(console, coordinates, [slime], XenobiologyTransferAnimationType.Suction);
            _popup.PopupEntity(Loc.GetString("xenobiology-console-slime-sent-to-grinder"), user, user);
            return true;
        }

        if (console.Comp.SlimeContainer.ContainedEntities.Count >= console.Comp.MaxStoredSlimes)
        {
            _popup.PopupEntity(
                Loc.GetString("xenobiology-console-slime-storage-full", ("amount", console.Comp.MaxStoredSlimes)),
                user,
                user);
            return false;
        }

        if (!_container.Insert(slime.Owner, console.Comp.SlimeContainer))
        {
            _popup.PopupEntity(Loc.GetString("xenobiology-console-slime-grab-failed"), user, user);
            return false;
        }

        StoreSlime(slime.Owner, console);
        UpdateViewState(console);
        PlayTransferAnimation(console, coordinates, [slime], XenobiologyTransferAnimationType.Suction);
        _popup.PopupEntity(
            Loc.GetString(
                "xenobiology-console-slime-grabbed",
                ("amount", console.Comp.SlimeContainer.ContainedEntities.Count),
                ("capacity", console.Comp.MaxStoredSlimes)),
            user,
            user);
        return true;
    }

    private bool TrySendToLinkedGrinder(
        Entity<XenobiologyConsoleComponent> console,
        Entity<SlimeComponent> slime)
    {
        if (!TryComp<DeviceLinkSourceComponent>(console, out var source))
        {
            return false;
        }

        foreach (var linked in source.LinkedPorts.Keys)
        {
            if (!_deviceLink.GetLinks(console, linked, source)
                    .Contains((console.Comp.GrinderOutputPort, console.Comp.GrinderInputPort)) ||
                !TryComp<SlimeGrinderComponent>(linked, out var grinder) ||
                !_slimeGrinder.TryQueueProcess(slime, (linked, grinder)))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool TryReleaseSlimes(
        EntityUid user,
        Entity<XenobiologyConsoleComponent> console,
        EntityCoordinates destination)
    {
        var released = ReleaseSlimes(console, destination);
        if (released.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("xenobiology-console-no-stored-slimes"), user, user);
            return false;
        }

        UpdateViewState(console);
        PlayTransferAnimation(console, destination, released, XenobiologyTransferAnimationType.Release);
        _popup.PopupEntity(
            Loc.GetString("xenobiology-console-slimes-released", ("amount", released.Count)),
            user,
            user);
        return true;
    }

    private void AnalyzeSlime(EntityUid user, Entity<SlimeComponent> slime)
    {
        _slimeScanner.SendSlimeScanToChat(user, slime);
    }

    private bool TryFindSlime(
        EntityUid remote,
        XenobiologyConsoleComponent console,
        Func<Entity<SlimeComponent>, bool> predicate,
        out Entity<SlimeComponent> slime)
    {
        var closestDistance = float.MaxValue;
        slime = default;

        foreach (var (candidate, distance) in FindSlimesWithDistance(remote, console, predicate))
        {
            if (distance >= closestDistance)
                continue;

            closestDistance = distance;
            slime = candidate;
        }

        return closestDistance < float.MaxValue;
    }

    private IEnumerable<(Entity<SlimeComponent> Slime, float Distance)> FindSlimesWithDistance(
        EntityUid remote,
        XenobiologyConsoleComponent console,
        Func<Entity<SlimeComponent>, bool> predicate)
    {
        var coords = Transform(remote).Coordinates;
        foreach (var candidate in _lookup.GetEntitiesInRange<SlimeComponent>(
                     coords,
                     console.SlimeTargetRange,
                     LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Approximate))
        {
            if (!predicate(candidate) ||
                !TryFindVisibleCamera(candidate, console, out _) ||
                !coords.TryDistance(EntityManager, Transform(candidate).Coordinates, out var distance))
            {
                continue;
            }

            yield return (candidate, distance);
        }
    }

    private bool TryFindMonkey(EntityUid remote, XenobiologyConsoleComponent console, out EntityUid monkey)
    {
        var coords = Transform(remote).Coordinates;
        var closestDistance = float.MaxValue;
        monkey = default;

        foreach (var candidate in _lookup.GetEntitiesInRange(coords, console.InteractionRange, LookupFlags.Dynamic | LookupFlags.Approximate))
        {
            if (MetaData(candidate).EntityPrototype?.ID != console.MonkeyPrototype.Id ||
                !TryFindVisibleCamera(candidate, console, out _) ||
                !coords.TryDistance(EntityManager, Transform(candidate).Coordinates, out var distance) ||
                distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            monkey = candidate;
        }

        return closestDistance < float.MaxValue;
    }

    private bool IsValidSlimeTarget(
        EntityUid remote,
        XenobiologyConsoleComponent console,
        EntityUid slime)
    {
        var remoteCoordinates = Transform(remote).Coordinates;
        return HasComp<SlimeComponent>(slime) &&
               !_container.IsEntityInContainer(slime) &&
               remoteCoordinates.TryDistance(EntityManager, Transform(slime).Coordinates, out var distance) &&
               distance <= console.SlimeTargetRange &&
               TryFindVisibleCamera(slime, console, out _);
    }

    private bool IsValidMonkeyTarget(
        EntityUid remote,
        XenobiologyConsoleComponent console,
        EntityUid monkey)
    {
        var remoteCoordinates = Transform(remote).Coordinates;
        return MetaData(monkey).EntityPrototype?.ID == console.MonkeyPrototype.Id &&
               !_container.IsEntityInContainer(monkey) &&
               remoteCoordinates.TryDistance(EntityManager, Transform(monkey).Coordinates, out var distance) &&
               distance <= console.InteractionRange &&
               TryFindVisibleCamera(monkey, console, out _);
    }

    private bool IsValidShortcutCoordinates(
        EntityUid remote,
        XenobiologyConsoleComponent console,
        EntityCoordinates coordinates)
    {
        var maxRange = MathF.Max(console.InteractionRange, console.SlimeTargetRange);
        return coordinates.IsValid(EntityManager) &&
               Transform(remote).Coordinates.TryDistance(EntityManager, coordinates, out var distance) &&
               distance <= maxRange &&
               TryFindVisibleCamera(coordinates, console, out _);
    }

    private static bool IsFloorTarget(EntityUid? target, EntityCoordinates coordinates)
    {
        return target == null || target == coordinates.EntityId;
    }
}

// SPDX-FileCopyrightText: 2026 Project Dumont
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Shared.Xenobiology;
using Content.Goobstation.Shared.Xenobiology.Components;
using Content.Server.NPC.HTN;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Events;
using Content.Shared.Pulling.Events;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Goobstation.Server.Xenobiology;

public sealed partial class XenobiologyConsoleSystem
{
    private void StoreSlime(EntityUid slime, Entity<XenobiologyConsoleComponent> console)
    {
        var stored = EnsureComp<XenobiologyConsoleStoredSlimeComponent>(slime);
        stored.Console = console;

        if (TryComp<HTNComponent>(slime, out var htn))
        {
            stored.HtnWasEnabled ??= htn.Enabled;
            _htn.SetHTNEnabled((slime, htn), false);
        }

        _npc.SleepNPC(slime);
        _actionBlocker.UpdateCanMove(slime);
    }

    private List<EntityUid> ReleaseSlimes(
        Entity<XenobiologyConsoleComponent> console,
        EntityCoordinates destination)
    {
        var released = new List<EntityUid>(console.Comp.SlimeContainer.ContainedEntities.Count);
        foreach (var slime in _container.EmptyContainer(console.Comp.SlimeContainer, destination: destination))
            released.Add(slime);

        return released;
    }

    private void PlayTransferAnimation(
        Entity<XenobiologyConsoleComponent> console,
        EntityCoordinates coordinates,
        List<EntityUid> targets,
        XenobiologyTransferAnimationType type)
    {
        if (targets.Count == 0)
            return;

        var sound = type == XenobiologyTransferAnimationType.Suction
            ? console.Comp.SuctionSound
            : console.Comp.ReleaseSound;
        _audio.PlayPvs(sound, coordinates);

        var animation = new XenobiologyTransferAnimationEvent(
            GetNetCoordinates(coordinates),
            GetNetEntityList(targets),
            type);
        RaiseNetworkEvent(animation, Filter.Pvs(coordinates, entityMan: EntityManager));
    }

    private void UpdateViewState(
        Entity<XenobiologyConsoleComponent> console,
        Entity<XenobiologyConsoleViewComponent>? viewEntity = null)
    {
        if (viewEntity == null)
        {
            if (console.Comp.ActiveUser is not { } user ||
                !TryComp<XenobiologyConsoleViewComponent>(user, out var view))
            {
                return;
            }

            viewEntity = (user, view);
        }

        var viewComp = viewEntity.Value.Comp;
        viewComp.StoredSlimes = console.Comp.SlimeContainer.ContainedEntities.Count;
        viewComp.MaxStoredSlimes = console.Comp.MaxStoredSlimes;
        viewComp.MonkeyBiomass = console.Comp.MonkeyBiomass;
        Dirty(viewEntity.Value.Owner, viewComp);
    }

    private void RestoreStoredSlime(EntityUid slime)
    {
        if (!TryComp<XenobiologyConsoleStoredSlimeComponent>(slime, out var stored))
            return;

        if (TryComp<HTNComponent>(slime, out var htn))
        {
            _htn.SetHTNEnabled((slime, htn), stored.HtnWasEnabled ?? true);
            if (htn.Enabled && !_mobState.IsDead(slime))
                _npc.WakeNPC(slime, htn);
        }

        RemComp<XenobiologyConsoleStoredSlimeComponent>(slime);
        _actionBlocker.UpdateCanMove(slime);
    }

    private void OnStoredSlimeMoveAttempt(Entity<XenobiologyConsoleStoredSlimeComponent> ent, ref UpdateCanMoveEvent args)
    {
        args.Cancel();
    }

    private void OnStoredSlimeAttackAttempt(Entity<XenobiologyConsoleStoredSlimeComponent> ent, ref AttackAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnStoredSlimeUseAttempt(Entity<XenobiologyConsoleStoredSlimeComponent> ent, ref UseAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnStoredSlimePullAttempt(Entity<XenobiologyConsoleStoredSlimeComponent> ent, ref StartPullAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnStoredSlimeShutdown(Entity<XenobiologyConsoleStoredSlimeComponent> ent, ref ComponentShutdown args)
    {
        if (Terminating(ent))
            return;

        if (TryComp<HTNComponent>(ent, out var htn) && ent.Comp.HtnWasEnabled is { } wasEnabled)
            _htn.SetHTNEnabled((ent.Owner, htn), wasEnabled);
    }
}

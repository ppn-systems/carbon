// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World.Construction;
using Carbon.Application.World.Replication;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Building;
using Carbon.Simulation.ECS;

namespace Carbon.Application.World;

/// <summary>
/// Server-authoritative construction: every place / cancel / deconstruct / damage request goes through
/// <see cref="BuildingService"/> (RimWorld-style lifecycle, see <c>docs/building.md</c>). Builders keep one job that
/// advances each fixed tick while they stay within reach.
/// </summary>
public sealed partial class AuthoritativeWorld
{
    /// <summary>Gets the structure definitions used by the server.</summary>
    public StructureCatalog StructureCatalog => _construction.Catalog;

    private bool TryGetBuildActorLocked(ActivePlayerState state, out BuildActor actor) =>
        _construction.TryCreateActor(state.Entity, state.SourceCharacter.Id, state.Inventory.Slots, out actor);

    private bool IsPlayerOnTileLocked(int tileX, int tileY)
    {
        foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
        {
            Entity entity = kvp.Value.Entity;
            if (!_simulation.Entities.IsAlive(entity) || !_simulation.Entities.HasTransform(entity))
            {
                continue;
            }

            Vector2 pos = _simulation.Entities.GetTransform(entity).Position;
            if ((int)MathF.Floor(pos.X / this.TileSize) == tileX && (int)MathF.Floor(pos.Y / this.TileSize) == tileY)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Places a blueprint for <paramref name="state"/>, delivers what the player carries and makes it the player's build job.
    /// </summary>
    private BuildMutationResult PlaceBlueprintLocked(ActivePlayerState state, int tileX, int tileY, StructureType type, StructureRotation rotation)
    {
        if (!this.TryGetBuildActorLocked(state, out BuildActor actor))
        {
            return BuildMutationResult.Fail(BuildResultCode.PermissionDenied);
        }

        _playerMaterials.Bind(state);
        return _construction.PlaceBlueprint(actor, state.Build, _playerMaterials, tileX, tileY, type, rotation);
    }

    /// <summary>Advances every player's construction or deconstruction job by one fixed tick.</summary>
    private void ProcessBuildJobsLocked(float dt)
    {
        float work = _construction.WorkFor(dt);
        foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
        {
            ActivePlayerState state = kvp.Value;
            if (state.Build.Kind == BuildJobKind.None || !this.TryGetBuildActorLocked(state, out BuildActor actor))
            {
                continue;
            }

            _playerMaterials.Bind(state);
            _construction.StepJob(state.Build, actor, _playerMaterials, work);
        }
    }

    /// <summary>
    /// <see cref="IMaterialSource"/> over one player's inventory. Rebound per call so ticks do not allocate; slot changes
    /// are queued for the owning client and refunds that do not fit drop as ground loot.
    /// </summary>
    private sealed class PlayerMaterialAdapter(AuthoritativeWorld world) : IMaterialSource
    {
        private ActivePlayerState? _state;

        public void Bind(ActivePlayerState state) => _state = state;

        public int CountOf(string itemId) =>
            _state is not null && world._construction.TryResolveItemId(itemId, out uint id, out _) ? _state.Inventory.CountItem(id) : 0;

        public int Take(string itemId, int quantity)
        {
            if (_state is null || !world._construction.TryResolveItemId(itemId, out uint id, out _))
            {
                return 0;
            }
            int available = Math.Min(quantity, _state.Inventory.CountItem(id));
            if (available <= 0)
            {
                return 0;
            }

            InventoryContainer inventory = _state.Inventory;
            Span<ItemSlot> before = stackalloc ItemSlot[inventory.Slots.Length];
            inventory.Slots.CopyTo(before);
            int removed = inventory.TryRemove(id, available);
            this.QueueSlotChanges(before);
            return removed;
        }

        public void Add(string itemId, int quantity)
        {
            if (_state is null || quantity <= 0 || !world._construction.TryResolveItemId(itemId, out uint id, out int maxStack))
            {
                return;
            }

            InventoryContainer inventory = _state.Inventory;
            Span<ItemSlot> before = stackalloc ItemSlot[inventory.Slots.Length];
            inventory.Slots.CopyTo(before);
            ItemSlot item = new(id, quantity);
            int remainder = PlayerInventoryLayout.TryAddBagItem(inventory, in item, maxStack);
            this.QueueSlotChanges(before);
            if (remainder > 0 && world._simulation.Entities.HasTransform(_state.Entity))
            {
                ItemSlot overflow = new(id, remainder);
                world.SpawnGroundLootLocked(world._simulation.Entities.GetTransform(_state.Entity).Position, in overflow);
            }
        }

        private void QueueSlotChanges(ReadOnlySpan<ItemSlot> before)
        {
            ActivePlayerState state = _state!;
            ReadOnlySpan<ItemSlot> after = state.Inventory.Slots;
            bool changed = false;
            for (int i = 0; i < after.Length; i++)
            {
                if (before[i] != after[i])
                {
                    world._outbound.Pending.SlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, (ushort)i, after[i]));
                    changed = true;
                }
            }

            if (changed)
            {
                world.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            }
        }
    }
}

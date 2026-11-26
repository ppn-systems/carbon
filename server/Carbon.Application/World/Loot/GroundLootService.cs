// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Items.Catalog;
using Carbon.Items.Components;
using Carbon.Items.Containers;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Loot;

/// <summary>A ground loot stack tracked by the world, keyed by its network id.</summary>
/// <param name="Item">The stack lying on the ground.</param>
/// <param name="SpawnTick">Tick the stack was dropped; auto-collect waits a short grace period after it.</param>
internal readonly record struct GroundLootEntry(ItemSlot Item, ulong SpawnTick);

/// <summary>
/// Owns ground loot on the authoritative server: spawning (merging into nearby stacks of the same item), finding
/// stacks a player walks over, updating or removing a stack after a pickup, and forgetting stacks whose entity died
/// (audit SOLID-1, first extraction from <see cref="AuthoritativeWorld"/>). Called only from inside the world tick lock.
/// </summary>
internal sealed class GroundLootService
{
    /// <summary>Physics layer of loot trigger colliders.</summary>
    public const uint LootLayer = 8U;

    /// <summary>Ticks a ground stack lives before it despawns (120 s at 30 Hz).</summary>
    public const ulong LootLifetimeTicks = 30UL * 120UL;

    private const float MergeRadius = 24f;
    private const float AutoCollectRadius = 32f;
    private const ulong AutoCollectGraceTicks = 12UL;

    private readonly WorldSimulation _simulation;
    private readonly Func<ItemRules> _itemRules;
    private readonly Func<ulong> _allocateNetworkId;
    private readonly Func<ulong> _currentTick;
    private readonly Action<ulong, Vector2> _onStackChanged;
    private readonly Dictionary<ulong, GroundLootEntry> _byNetworkId = new();
    private readonly List<ulong> _scratch = new(32);

    /// <summary>Initializes a new instance of the <see cref="GroundLootService"/> class.</summary>
    /// <param name="simulation">The world simulation that owns the loot entities.</param>
    /// <param name="itemRules">Current item rules (they can be swapped by a config reload).</param>
    /// <param name="allocateNetworkId">Allocates a fresh network id for a new loot entity.</param>
    /// <param name="currentTick">Current authoritative tick.</param>
    /// <param name="onStackChanged">Called when an existing stack's contents change, so observers can be refreshed.</param>
    public GroundLootService(
        WorldSimulation simulation,
        Func<ItemRules> itemRules,
        Func<ulong> allocateNetworkId,
        Func<ulong> currentTick,
        Action<ulong, Vector2> onStackChanged)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _itemRules = itemRules ?? throw new ArgumentNullException(nameof(itemRules));
        _allocateNetworkId = allocateNetworkId ?? throw new ArgumentNullException(nameof(allocateNetworkId));
        _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
        _onStackChanged = onStackChanged ?? throw new ArgumentNullException(nameof(onStackChanged));
    }

    /// <summary>Gets the number of tracked ground stacks.</summary>
    public int Count => _byNetworkId.Count;

    /// <summary>Gets the tracked stack for <paramref name="networkId"/>.</summary>
    public bool TryGet(ulong networkId, out GroundLootEntry entry) => _byNetworkId.TryGetValue(networkId, out entry);

    /// <summary>
    /// Drops <paramref name="item"/> at <paramref name="worldPos"/>, topping up a nearby stack of the same stackable item
    /// first and spawning a new loot entity for whatever does not fit.
    /// </summary>
    public void Spawn(Vector2 worldPos, in ItemSlot item)
    {
        if (item.IsEmpty)
        {
            return;
        }

        int maxStack = _itemRules().GetMaxStackSize(item.ItemId);
        if (maxStack > 1 && item.IsStackable)
        {
            foreach (KeyValuePair<ulong, GroundLootEntry> kvp in _byNetworkId)
            {
                GroundLootEntry existing = kvp.Value;
                if (existing.Item.ItemId != item.ItemId || !existing.Item.IsStackable || existing.Item.Quantity >= maxStack)
                {
                    continue;
                }

                if (!_simulation.TryGetEntityByNetworkId(kvp.Key, out Entity existingEnt) ||
                    !_simulation.Entities.IsAlive(existingEnt) ||
                    !_simulation.Entities.HasTransform(existingEnt))
                {
                    continue;
                }

                Vector2 existingPos = _simulation.Entities.GetTransform(existingEnt).Position;
                if (Vector2.DistanceSquared(existingPos, worldPos) <= (MergeRadius * MergeRadius))
                {
                    int room = maxStack - existing.Item.Quantity;
                    int added = Math.Min(room, item.Quantity);
                    ItemSlot merged = existing.Item with { Quantity = existing.Item.Quantity + added };
                    _byNetworkId[kvp.Key] = existing with { Item = merged };
                    _simulation.Entities.SetComponent(existingEnt, new LootDropComponent(merged, 0, _currentTick() + LootLifetimeTicks));
                    _onStackChanged(kvp.Key, existingPos);

                    int leftQty = item.Quantity - added;
                    if (leftQty <= 0)
                    {
                        return;
                    }

                    ItemSlot overflow = item with { Quantity = leftQty };
                    this.SpawnNewEntity(worldPos, in overflow);
                    return;
                }
            }
        }

        this.SpawnNewEntity(worldPos, in item);
    }

    /// <summary>
    /// Adds to <paramref name="output"/> the stacks within auto-collect range of <paramref name="position"/> that are past
    /// their drop grace period.
    /// </summary>
    public void CollectAutoPickupCandidates(Vector2 position, List<ulong> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        ulong now = _currentTick();
        foreach (KeyValuePair<ulong, GroundLootEntry> kv in _byNetworkId)
        {
            if (now < kv.Value.SpawnTick + AutoCollectGraceTicks)
            {
                continue;
            }

            if (_simulation.TryGetEntityByNetworkId(kv.Key, out Entity lootEnt) &&
                _simulation.Entities.IsAlive(lootEnt) &&
                _simulation.Entities.HasTransform(lootEnt) &&
                Vector2.DistanceSquared(position, _simulation.Entities.GetTransform(lootEnt).Position) <= (AutoCollectRadius * AutoCollectRadius))
            {
                output.Add(kv.Key);
            }
        }
    }

    /// <summary>
    /// Records that a pickup left <paramref name="remainder"/> units of the stack: removes and despawns it at zero,
    /// otherwise shrinks it, renews its lifetime and refreshes observers.
    /// </summary>
    public void ApplyPickup(ulong networkId, Entity lootEntity, Vector2 lootPosition, in GroundLootEntry entry, int remainder)
    {
        if (remainder <= 0)
        {
            _ = _byNetworkId.Remove(networkId);
            _simulation.DespawnEntity(lootEntity);
            return;
        }

        ItemSlot left = entry.Item with { Quantity = remainder };
        _byNetworkId[networkId] = entry with { Item = left };
        _simulation.Entities.SetComponent(lootEntity, new LootDropComponent(left, 0, _currentTick() + LootLifetimeTicks));
        _onStackChanged(networkId, lootPosition);
    }

    /// <summary>Forgets <paramref name="networkId"/> (its entity was despawned).</summary>
    public void Forget(ulong networkId) => _ = _byNetworkId.Remove(networkId);

    /// <summary>Forgets every stack whose entity is no longer alive (for example, expired by the loot system).</summary>
    public void ForgetDeadStacks()
    {
        if (_byNetworkId.Count == 0)
        {
            return;
        }

        _scratch.Clear();
        foreach (KeyValuePair<ulong, GroundLootEntry> kv in _byNetworkId)
        {
            if (!_simulation.TryGetEntityByNetworkId(kv.Key, out Entity ent) || !_simulation.Entities.IsAlive(ent))
            {
                _scratch.Add(kv.Key);
            }
        }

        for (int i = 0; i < _scratch.Count; i++)
        {
            _ = _byNetworkId.Remove(_scratch[i]);
        }
    }

    private void SpawnNewEntity(Vector2 worldPos, in ItemSlot item)
    {
        ulong netId = _allocateNetworkId();
        ulong now = _currentTick();
        Entity lootEntity = _simulation.SpawnNetworkEntity(netId, worldPos, 8f);
        _simulation.Entities.SetCollider(lootEntity, new Collider2D(ColliderKind.Circle, 8f, default, IsTrigger: true, LootLayer, 0U));
        _simulation.Entities.SetComponent(lootEntity, new LootDropComponent(item, 0, now + LootLifetimeTicks));
        _byNetworkId[netId] = new GroundLootEntry(item, now);
    }
}

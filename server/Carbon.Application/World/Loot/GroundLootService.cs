// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World.Replication;
using Carbon.Contracts.Entities;
using Carbon.Economy.Currency;
using Carbon.Items.Catalog;
using Carbon.Items.Components;
using Carbon.Items.Containers;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Loot;

/// <summary>Who may pick up a kill's loot, and for how long it stays on the ground.</summary>
/// <param name="OwnerCharacterId">Character that alone may pick it up while protected (<see cref="Guid.Empty"/> = anyone).</param>
/// <param name="OwnerNetworkId">The owner's player network id at the kill; replicated while the owner is offline.</param>
/// <param name="ProtectedUntilTick">First tick at which anyone may pick it up.</param>
/// <param name="LifetimeTicks">Ticks the loot lies on the ground (renewed after a partial pickup).</param>
internal readonly record struct LootClaim(Guid OwnerCharacterId, ulong OwnerNetworkId, ulong ProtectedUntilTick, ulong LifetimeTicks);

/// <summary>A ground loot stack tracked by the world, keyed by its network id.</summary>
/// <param name="Item">The stack lying on the ground (empty for a currency pile).</param>
/// <param name="SpawnTick">Tick the stack was dropped; auto-collect waits a short grace period after it.</param>
/// <param name="OwnerCharacterId">Character that alone may pick it up until <paramref name="ProtectedUntilTick"/> (<see cref="Guid.Empty"/> = anyone).
/// Keyed by character, so the owner keeps the reservation across a reconnect.</param>
/// <param name="OwnerNetworkId">The owner's player network id at the kill; replicated while the owner is offline.</param>
/// <param name="ProtectedUntilTick">First tick at which anyone may pick it up.</param>
/// <param name="LifetimeTicks">Ticks the stack lies on the ground.</param>
/// <param name="Currency">Currency of a coin or diamond pile; 0 for an item stack.</param>
/// <param name="CurrencyAmount">Amount a currency pile credits to the wallet.</param>
/// <param name="CreditReason">Ledger reason of the credit when a currency pile is picked up.</param>
/// <param name="DroppedBy">Character that threw the stack out of its bag; its auto-collect skips the stack until it walks away.</param>
/// <param name="DropperRearmed">Whether <paramref name="DroppedBy"/> has left the stack once, so walking over it collects it again.</param>
internal readonly record struct GroundLootEntry(
    ItemSlot Item,
    ulong SpawnTick,
    Guid OwnerCharacterId = default,
    ulong OwnerNetworkId = 0UL,
    ulong ProtectedUntilTick = 0UL,
    ulong LifetimeTicks = GroundLootService.LootLifetimeTicks,
    CurrencyKind Currency = 0,
    long CurrencyAmount = 0L,
    CurrencyReason CreditReason = CurrencyReason.MonsterLoot,
    Guid DroppedBy = default,
    bool DropperRearmed = false)
{
    /// <summary>Gets whether this is a gold or diamond pile rather than an item stack.</summary>
    public bool IsCurrency => this.Currency != 0;

    /// <summary>Gets whether the stack is still reserved for its owner at <paramref name="tick"/>.</summary>
    public bool IsProtectedAt(ulong tick) => this.OwnerCharacterId != Guid.Empty && tick < this.ProtectedUntilTick;

    /// <summary>Gets whether character <paramref name="characterId"/> may pick the stack up at <paramref name="tick"/>.</summary>
    public bool CanBePickedUpBy(Guid characterId, ulong tick) => !this.IsProtectedAt(tick) || characterId == this.OwnerCharacterId;
}

/// <summary>
/// Owns ground loot on the authoritative server: spawning (merging into nearby stacks of the same item), finding
/// stacks a player walks over, updating or removing a stack after a pickup, and forgetting stacks whose entity died
/// (audit SOLID-1, first extraction from <see cref="AuthoritativeWorld"/>). Kill loot (item stacks and gold/diamond piles)
/// carries a <see cref="LootClaim"/>: it never merges, only the killer may take it while protected, and it lives the
/// claim's lifetime (ADR-019). Called only from inside the world tick lock.
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

    /// <summary>How far the dropper must walk from a stack it threw away before walking over it collects it again.</summary>
    private const float DropperRearmRadius = 48f;

    private readonly WorldSimulation _simulation;
    private readonly Func<ItemRules> _itemRules;
    private readonly Func<ulong> _allocateNetworkId;
    private readonly Func<ulong> _currentTick;
    private readonly Action<ulong, Vector2> _onStackChanged;
    private readonly Func<Guid, ulong> _ownerNetworkId;
    private readonly Dictionary<ulong, GroundLootEntry> _byNetworkId = new();
    private readonly List<ulong> _scratch = new(32);
    private readonly List<ulong> _rearmScratch = new(8);

    /// <summary>Initializes a new instance of the <see cref="GroundLootService"/> class.</summary>
    /// <param name="simulation">The world simulation that owns the loot entities.</param>
    /// <param name="itemRules">Current item rules (they can be swapped by a config reload).</param>
    /// <param name="allocateNetworkId">Allocates a fresh network id for a new loot entity.</param>
    /// <param name="currentTick">Current authoritative tick.</param>
    /// <param name="onStackChanged">Called when an existing stack's contents change, so observers can be refreshed.</param>
    /// <param name="ownerNetworkId">
    /// Current player network id of an online character (0 when offline), so a reconnected owner is replicated under its
    /// new id. Defaults to "offline", which replicates the id recorded at the kill.
    /// </param>
    public GroundLootService(
        WorldSimulation simulation,
        Func<ItemRules> itemRules,
        Func<ulong> allocateNetworkId,
        Func<ulong> currentTick,
        Action<ulong, Vector2> onStackChanged,
        Func<Guid, ulong>? ownerNetworkId = null)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _itemRules = itemRules ?? throw new ArgumentNullException(nameof(itemRules));
        _allocateNetworkId = allocateNetworkId ?? throw new ArgumentNullException(nameof(allocateNetworkId));
        _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
        _onStackChanged = onStackChanged ?? throw new ArgumentNullException(nameof(onStackChanged));
        _ownerNetworkId = ownerNetworkId ?? (static _ => 0UL);
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
                if (existing.OwnerCharacterId != Guid.Empty || existing.IsCurrency ||
                    existing.Item.ItemId != item.ItemId || !existing.Item.IsStackable || existing.Item.Quantity >= maxStack)
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
    /// Drops a stack a player threw out of its bag at <paramref name="worldPos"/> under <paramref name="claim"/>, the same
    /// rules as kill loot (ADR-019): its own entity (never merged), only the dropper may take it while protected, and it
    /// lives the claim's lifetime. The dropper's own auto-collect also ignores it until the dropper walks away.
    /// </summary>
    public void SpawnDropped(Vector2 worldPos, in ItemSlot item, in LootClaim claim)
    {
        if (item.IsEmpty)
        {
            return;
        }

        ulong netId = this.SpawnEntity(worldPos, in item, in claim, 0, 0L);
        Guid dropperCharacterId = claim.OwnerCharacterId;
        _byNetworkId[netId] = _byNetworkId[netId] with { DroppedBy = dropperCharacterId };
    }

    /// <summary>
    /// Drops a kill's item stack at <paramref name="worldPos"/> as its own entity (never merged), reserved for the claim's
    /// owner while protected and living the claim's lifetime.
    /// </summary>
    public void SpawnClaimed(Vector2 worldPos, in ItemSlot item, in LootClaim claim)
    {
        if (!item.IsEmpty)
        {
            _ = this.SpawnEntity(worldPos, in item, in claim, 0, 0L);
        }
    }

    /// <summary>
    /// Drops a pile of <paramref name="amount"/> gold or diamonds at <paramref name="worldPos"/> under <paramref name="claim"/>;
    /// picking it up credits the wallet with <paramref name="creditReason"/>.
    /// </summary>
    public void SpawnCurrency(Vector2 worldPos, CurrencyKind currency, long amount, in LootClaim claim, CurrencyReason creditReason = CurrencyReason.MonsterLoot)
    {
        if (amount > 0L && currency != 0)
        {
            _ = this.SpawnEntity(worldPos, ItemSlot.Empty, in claim, currency, amount, creditReason);
        }
    }

    /// <summary>
    /// Builds the replicated identity of ground stack <paramref name="networkId"/>: item or currency, quantity, and the owner
    /// with the protection left (in milliseconds at <paramref name="tickRate"/> Hz).
    /// </summary>
    public bool TryGetSpawnIdentity(ulong networkId, ushort tickRate, out SpawnIdentity identity)
    {
        if (!_byNetworkId.TryGetValue(networkId, out GroundLootEntry entry))
        {
            identity = default;
            return false;
        }

        ulong now = _currentTick();
        bool isProtected = entry.IsProtectedAt(now);
        uint protectionMs = isProtected
            ? (uint)Math.Min(uint.MaxValue, (entry.ProtectedUntilTick - now) * 1000UL / Math.Max((ushort)1, tickRate))
            : 0U;
        ItemSlot item = entry.Item;
        identity = SpawnIdentity.Unknown with
        {
            Kind = EntityKind.LootDrop,
            Name = entry.Currency switch
            {
                CurrencyKind.Gold => "Gold",
                CurrencyKind.Diamond => "Diamonds",
                _ => _itemRules().GetDisplayName(item.ItemId),
            },
            Archetype = entry.IsCurrency ? (ushort)0 : (ushort)item.ItemId,
            LootCount = entry.IsCurrency ? (int)Math.Min(int.MaxValue, entry.CurrencyAmount) : item.Quantity,
            WeaponUpgrade = entry.IsCurrency ? (byte)0 : ItemEnhancementPolicy.GetUpgradeLevel(in item),
            LootCurrency = (LootCurrency)entry.Currency,
            LootOwnerNetworkId = isProtected ? this.ResolveOwnerNetworkId(in entry) : 0UL,
            LootProtectionMs = protectionMs,
        };
        return true;
    }

    /// <summary>
    /// Adds to <paramref name="output"/> the stacks within auto-collect range of <paramref name="position"/> that are past
    /// their drop grace period. Stacks <paramref name="collector"/> threw away are skipped until it has walked beyond
    /// <see cref="DropperRearmRadius"/> of them once, so a dropped item is not sucked straight back into the bag.
    /// </summary>
    public void CollectAutoPickupCandidates(Vector2 position, Guid collector, List<ulong> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        ulong now = _currentTick();
        _rearmScratch.Clear();
        foreach (KeyValuePair<ulong, GroundLootEntry> kv in _byNetworkId)
        {
            if (!_simulation.TryGetEntityByNetworkId(kv.Key, out Entity lootEnt) ||
                !_simulation.Entities.IsAlive(lootEnt) ||
                !_simulation.Entities.HasTransform(lootEnt))
            {
                continue;
            }

            float distanceSq = Vector2.DistanceSquared(position, _simulation.Entities.GetTransform(lootEnt).Position);
            if (collector != Guid.Empty && kv.Value.DroppedBy == collector && !kv.Value.DropperRearmed)
            {
                if (distanceSq > DropperRearmRadius * DropperRearmRadius)
                {
                    _rearmScratch.Add(kv.Key);
                }

                continue;
            }

            if (now >= kv.Value.SpawnTick + AutoCollectGraceTicks && distanceSq <= AutoCollectRadius * AutoCollectRadius)
            {
                output.Add(kv.Key);
            }
        }

        for (int i = 0; i < _rearmScratch.Count; i++)
        {
            ulong key = _rearmScratch[i];
            _byNetworkId[key] = _byNetworkId[key] with { DropperRearmed = true };
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
        _simulation.Entities.SetComponent(lootEntity, new LootDropComponent(left, 0, _currentTick() + entry.LifetimeTicks));
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

    private void SpawnNewEntity(Vector2 worldPos, in ItemSlot item) =>
        _ = this.SpawnEntity(worldPos, in item, new LootClaim(Guid.Empty, 0UL, 0UL, LootLifetimeTicks), 0, 0L);

    private ulong SpawnEntity(Vector2 worldPos, in ItemSlot item, in LootClaim claim, CurrencyKind currency, long amount, CurrencyReason creditReason = CurrencyReason.MonsterLoot)
    {
        ulong netId = _allocateNetworkId();
        ulong now = _currentTick();
        Entity lootEntity = _simulation.SpawnNetworkEntity(netId, worldPos, 8f);
        _simulation.Entities.SetCollider(lootEntity, new Collider2D(ColliderKind.Circle, 8f, default, IsTrigger: true, LootLayer, 0U));
        _simulation.Entities.SetComponent(lootEntity, new LootDropComponent(item, 0, now + claim.LifetimeTicks));
        _byNetworkId[netId] = new GroundLootEntry(item, now, claim.OwnerCharacterId, claim.OwnerNetworkId, claim.ProtectedUntilTick, claim.LifetimeTicks, currency, amount, creditReason);
        return netId;
    }

    private ulong ResolveOwnerNetworkId(in GroundLootEntry entry)
    {
        ulong current = _ownerNetworkId(entry.OwnerCharacterId);
        return current != 0UL ? current : entry.OwnerNetworkId;
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World.Construction;
using Carbon.Combat.Cooldowns;
using Carbon.Contracts.Entities;
using Carbon.Economy.Currency;
using Carbon.Economy.Loot;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Simulation.ECS;

namespace Carbon.Application.World.Players;

/// <summary>
/// Server-side state of one player in the world: session, entity, inventory, pending input, progression, stats,
/// survival and build state, wallet. Owned by <see cref="AuthoritativeWorld"/> and only touched under its lock.
/// </summary>
internal sealed class PlayerState(
    ConnectedPlayerSession session,
    Entity entity,
    CharacterRecord sourceCharacter,
    InventoryContainer inventory)
{
    public ConnectedPlayerSession Session { get; set; } = session;
    public Entity Entity { get; } = entity;
    public CharacterRecord SourceCharacter { get; set; } = sourceCharacter;
    public InventoryContainer Inventory { get; } = inventory;
    public CooldownTracker Cooldowns { get; } = new();
    public Vector2 PendingDirection { get; set; }
    public Vector2 LastSavedPosition { get; set; }
    public bool PendingSprint { get; set; }
    public uint LastClientTick { get; set; }
    public bool HasPendingInput { get; set; }
    public bool HasPendingAttack { get; set; }
    public ulong PendingAttackTargetId { get; set; }
    public Vector2 PendingAttackAim { get; set; }
    public byte PendingAttackAbility { get; set; }
    public ulong PendingPickupLootId { get; set; }
    /// <summary>Gets or sets the last tick this player took or dealt combat damage (gates out-of-combat regeneration).</summary>
    public ulong LastCombatDamageTick { get; set; }
    public ushort Level { get; set; } = 1;
    public uint Experience { get; set; }
    public CharacterProgressionState Progression { get; } = new();
    public uint CombatRollNonce { get; set; }
    public DerivedCharacterStats Stats { get; set; } = new(100f, 80f, 100f, 2f, 1f, 1f);
    public float CurrentMana { get; set; } = 80f;
    public float CurrentStamina { get; set; } = 100f;
    public bool StatsDirty { get; set; }
    public bool LeveledUpPending { get; set; }
    public bool PersistenceDirty { get; set; }

    /// <summary>Gets or sets the status effect buffer version last sent to the owner (ADR-029 D9); max forces a send.</summary>
    public uint StatusSyncedVersion { get; set; } = uint.MaxValue;

    /// <summary>Gets or sets the derived status signature last sent to the owner; -1 forces a send.</summary>
    public int StatusSyncedDerived { get; set; } = -1;
    public bool InventoryDirty { get; set; }
    public int EatingSlot { get; set; } = -1;
    public uint EatingItemId { get; set; }
    public RestMode RestMode { get; set; }
    public int RestBedTileX { get; set; }
    public int RestBedTileY { get; set; }
    public BuildJob Build { get; } = new();

    /// <summary>Server-authoritative gold/diamond balances (in-memory until a persistence ADR lands).</summary>
    public Wallet Wallet { get; init; } = new(session.PlayerNetworkId);

    /// <summary>Diamond bad-luck protection counter.</summary>
    public LootPityState LootPity { get; set; }
}

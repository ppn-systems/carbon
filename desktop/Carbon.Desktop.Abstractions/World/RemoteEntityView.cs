// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Render-friendly snapshot of another player or NPC replicated into the local Area of Interest.
/// </summary>
/// <param name="NetworkId">Authoritative network ID.</param>
/// <param name="Kind">Entity kind.</param>
/// <param name="Name">Display name.</param>
/// <param name="Position">Latest smoothed world position.</param>
/// <param name="Velocity">Latest linear velocity.</param>
/// <param name="Radius">Collider radius in world units.</param>
/// <param name="CurrentHealth">Current health points.</param>
/// <param name="MaxHealth">Maximum health points.</param>
/// <param name="ArchetypeId">Monster, NPC or item archetype ID (0 for players).</param>
/// <param name="FacingDirection">Normalized facing direction vector.</param>
/// <param name="AttackAnimRemaining">Remaining attack animation seconds (&gt; 0 while attacking).</param>
/// <param name="AttackAnimDuration">Total attack animation duration in seconds.</param>
/// <param name="HitFlashRemaining">Remaining white damage flash duration in seconds.</param>
/// <param name="EquippedHeadId">Equipped head armor item ID (0 when none).</param>
/// <param name="EquippedChestId">Equipped chest armor item ID (0 when none).</param>
/// <param name="EquippedLegsId">Equipped leg armor item ID (0 when none).</param>
/// <param name="EquippedFeetId">Equipped footwear armor item ID (0 when none).</param>
/// <param name="EquippedWeaponId">Active held weapon or tool item ID.</param>
/// <param name="Level">Entity display level.</param>
/// <param name="LootCount">Stack quantity when representing a ground loot drop.</param>
/// <param name="WeaponUpgradeLevel">Enhancement level (<c>0..10</c>) of the equipped weapon or loot drop.</param>
/// <param name="ActiveActionKind">Current attack or skill action kind being animated.</param>
/// <param name="Attack">Server-announced monster attack phase (inactive by default).</param>
/// <param name="LootCurrency">For a ground loot drop: an item stack, or a gold or diamond pile of <c>LootCount</c> (ADR-019).</param>
/// <param name="IsLootLocked">Whether a ground loot drop is still reserved for another player (ADR-019).</param>
/// <param name="ComboStep">Combo step of the current swing (see <see cref="AttackComboCounter"/>).</param>
/// <param name="Look">A player's race, body type and option choices (ADR-042); default for other kinds.</param>
public readonly record struct RemoteEntityView(
    ulong NetworkId,
    EntityKind Kind,
    string Name,
    Vector2 Position,
    Vector2 Velocity,
    float Radius,
    float CurrentHealth,
    float MaxHealth,
    ushort ArchetypeId = 0,
    Vector2 FacingDirection = default,
    float AttackAnimRemaining = 0f,
    float AttackAnimDuration = 0.30f,
    float HitFlashRemaining = 0f,
    ushort EquippedHeadId = 0,
    ushort EquippedChestId = 0,
    ushort EquippedLegsId = 0,
    ushort EquippedFeetId = 0,
    ushort EquippedWeaponId = 0,
    ushort Level = 1,
    int LootCount = 0,
    byte WeaponUpgradeLevel = 0,
    ActionAnimationKind ActiveActionKind = ActionAnimationKind.None,
    AttackPhaseView Attack = default,
    LootCurrency LootCurrency = LootCurrency.None,
    bool IsLootLocked = false,
    byte ComboStep = 0,
    CharacterLook Look = default);

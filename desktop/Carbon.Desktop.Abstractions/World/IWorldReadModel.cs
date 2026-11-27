// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Items.Containers;
using Carbon.Maps.Building;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Strictly read-only world, actor vitals, inventory, action, and transient feedback query port (CQRS / ISP).
/// Renderers and HUD widgets depend only on this interface and cannot mutate network or gameplay state.
/// </summary>
public interface IWorldReadModel : IConnectionStatus, ICharacterProfile
{
    /// <summary>Gets the session's server-synced item and recipe tables (names, stats, stack sizes, recipes).</summary>
    SyncedItemCatalog Items { get; }

    /// <summary>Gets the chunk world coordinator backing terrain, props, and structures.</summary>
    ChunkWorld2D ChunkWorld { get; }

    /// <summary>Gets the client-side fixed-tick simulation engine.</summary>
    WorldSimulation Simulation { get; }

    /// <summary>Gets the local player's ECS entity handle.</summary>
    Entity LocalPlayer { get; }

    /// <summary>Gets the frame-interpolated local player world position (with prediction smoothing).</summary>
    Vector2 LocalPlayerInterpolatedPosition { get; }

    /// <summary>Gets the local player's current health.</summary>
    float LocalPlayerHealth { get; }

    /// <summary>Gets the local player's maximum health.</summary>
    float LocalPlayerMaxHealth { get; }

    /// <summary>Gets the local player's progression level (<c>1..99</c>).</summary>
    ushort LocalPlayerLevel { get; }

    /// <summary>Gets accumulated experience points toward the next level.</summary>
    uint LocalPlayerExperience { get; }

    /// <summary>Gets XP threshold required to reach the next level.</summary>
    uint LocalPlayerXpToNext { get; }

    /// <summary>Gets current mana points.</summary>
    float LocalPlayerMana { get; }

    /// <summary>Gets derived maximum mana points.</summary>
    float LocalPlayerMaxMana { get; }

    /// <summary>Gets current stamina points.</summary>
    float LocalPlayerStamina { get; }

    /// <summary>Gets derived maximum stamina points.</summary>
    float LocalPlayerMaxStamina { get; }

    /// <summary>Gets derived armor defense rating.</summary>
    float LocalPlayerDefense { get; }

    /// <summary>Gets the server-authoritative gold balance from the last <c>WalletSync</c>.</summary>
    long LocalPlayerGold { get; }

    /// <summary>Gets the server-authoritative diamond balance from the last <c>WalletSync</c>.</summary>
    long LocalPlayerDiamonds { get; }

    /// <summary>Gets the local player's food level (hunger meter).</summary>
    float LocalPlayerFood { get; }

    /// <summary>Gets the local player's food capacity.</summary>
    float LocalPlayerMaxFood { get; }

    /// <summary>Gets the hunger state: <c>0</c>=Fed, <c>1</c>=Hungry, <c>2</c>=UrgentlyHungry, <c>3</c>=Starving.</summary>
    byte LocalPlayerHungerState { get; }

    /// <summary>Gets malnutrition severity, <c>0..1</c>.</summary>
    float LocalPlayerMalnutrition { get; }

    /// <summary>Gets the local player's mood, <c>0..100</c>.</summary>
    float LocalPlayerMood { get; }

    /// <summary>Gets a value indicating whether the local player has food poisoning.</summary>
    bool LocalPlayerFoodPoisoned { get; }

    /// <summary>Gets the progress of the meal being eaten (<c>0..1</c>), or a negative value when not eating.</summary>
    float LocalPlayerEatProgress { get; }

    /// <summary>Gets the local player's rest level (ADR-018).</summary>
    float LocalPlayerRest { get; }

    /// <summary>Gets the local player's rest capacity.</summary>
    float LocalPlayerMaxRest { get; }

    /// <summary>Gets the rest state: <c>0</c>=Rested, <c>1</c>=Tired, <c>2</c>=VeryTired, <c>3</c>=Exhausted.</summary>
    byte LocalPlayerRestState { get; }

    /// <summary>Gets how the local player is resting.</summary>
    Contracts.Entities.RestMode LocalPlayerRestMode { get; }

    /// <summary>Gets remaining cooldown in seconds for active skill <paramref name="skillId"/>.</summary>
    float GetSkillCooldown(ushort skillId);

    /// <summary>Gets the character's persisted archetype class.</summary>
    HeroArchetypeClass LocalPlayerClass { get; }

    /// <summary>Gets persisted character progression traits bitmask.</summary>
    CharacterTraits LocalCharacterTraits { get; }

    /// <summary>Gets the unlocked skill bitmask.</summary>
    CharacterSkillMask LocalUnlockedSkillMask { get; }

    /// <summary>Gets unspent character attribute points earned from leveling.</summary>
    ushort LocalUnspentStatPoints { get; }

    /// <summary>Gets unspent skill mastery points earned from leveling.</summary>
    ushort LocalUnspentSkillPoints { get; }

    /// <summary>Gets allocated attribute points for <paramref name="attribute"/>.</summary>
    ushort GetAttributePoints(PrimaryAttributeId attribute);

    /// <summary>Gets learned mastery rank (<c>0</c>=locked, <c>1..MaxRank</c>=learned) for <paramref name="skillId"/>.</summary>
    byte GetSkillRank(ushort skillId);

    /// <summary>Gets the active zero-based hotbar slot index (<c>0..8</c>).</summary>
    byte SelectedHotbarSlot { get; }

    /// <summary>Gets the local player's active inventory container.</summary>
    InventoryContainer Inventory { get; }

    /// <summary>Gets the normalized direction vector of the most recent swing/attack.</summary>
    Vector2 LastAttackDirection { get; }

    /// <summary>Gets remaining visual swing arc timer in seconds (&gt; 0 while swinging).</summary>
    float AttackSwingTimer { get; }

    /// <summary>Gets the normalized facing direction of the local player.</summary>
    Vector2 LocalFacingDirection { get; }

    /// <summary>Gets remaining white hit-flash seconds when the local player takes damage.</summary>
    float LocalHitFlashTimer { get; }

    /// <summary>Gets the semantic visual action state for vector rig animation and VFX selection.</summary>
    ActionState CurrentAction { get; }

    /// <summary>Gets the remote players, monsters, and NPCs currently visible in the Area of Interest.</summary>
    IReadOnlyList<RemoteEntityView> RemoteEntities { get; }

    /// <summary>Gets a value indicating whether the world and local avatar are initialized.</summary>
    bool IsReady { get; }

    /// <summary>Gets the active world seed.</summary>
    ulong WorldSeed { get; }

    /// <summary>
    /// Gets the authoritative synchronized in-game time-of-day hour (<c>0..24</c>), or <see langword="null"/> when running offline.
    /// </summary>
    float? AuthoritativeTimeHour => null;

    /// <summary>Gets the network ID of the currently tab/click-locked hostile target (<c>0</c> if none).</summary>
    ulong LockedTargetNetworkId { get; }

    /// <summary>Gets remaining seconds for the forge outcome feedback pulse (&gt; 0 after upgrade attempt).</summary>
    float ForgeFeedbackTimer { get; }

    /// <summary>Gets whether the most recent forge enhancement attempt succeeded.</summary>
    bool LastForgeSucceeded { get; }

    /// <summary>Gets the server-rolled outcome of the most recent forge attempt (ADR-024).</summary>
    EnhancementOutcome LastForgeOutcome => EnhancementOutcome.None;

    /// <summary>Gets the item level after the most recent forge attempt (<c>0</c> when destroyed).</summary>
    byte LastForgeNewLevel => 0;

    /// <summary>Gets a counter that increases with every forge result, so the UI can start one outcome effect per result.</summary>
    uint ForgeResultSerial => 0U;

    /// <summary>Copies the player's inventory slots into <paramref name="destination"/> under session synchronization.</summary>
    void CopyInventorySlots(Span<ItemSlot> destination);

    /// <summary>Gets the open trader window's stock (ADR-025), or <see langword="null"/> when no trader is open.</summary>
    TraderView? ActiveTrader { get; }

    /// <summary>Returns <see langword="true"/> when a Blacksmith NPC or placed <c>ForgeAnvil</c> is nearby.</summary>
    bool IsNearForgeStation(float maxDistance = 175f);

    /// <summary>Drains newly buffered floating combat/loot events into <paramref name="destination"/>.</summary>
    void DrainCombatEvents(List<CombatFeedbackEvent> destination);

    /// <summary>Drains newly buffered prop impact/fracture events into <paramref name="destination"/>.</summary>
    void DrainPropImpactEvents(List<PropImpactEvent> destination);

    /// <summary>Drains chunk coordinates invalidated by incoming network prop/structure/delta updates.</summary>
    void DrainDirtyChunks(List<(int ChunkX, int ChunkY)> destination);

    /// <summary>Drains recent loot, craft, or warning toast notifications for the HUD feed.</summary>
    void DrainHudNotifications(List<string> destination);

    /// <summary>
    /// Copies the construction sites and deconstructions currently known to the client (render-only snapshot).
    /// </summary>
    /// <param name="destination">Cleared and filled with the markers.</param>
    void CopyConstructionMarkers(List<ConstructionMarker> destination);

    /// <summary>
    /// Previews a blueprint placement with the same rules the server applies (<see cref="BuildingService.ValidatePlacement"/>)
    /// against the client's copy of the world. The server re-validates every request.
    /// </summary>
    /// <param name="tileX">Anchor world tile X.</param>
    /// <param name="tileY">Anchor world tile Y.</param>
    /// <param name="type">Structure type.</param>
    /// <param name="rotation">Placement rotation.</param>
    /// <param name="width">Rotated footprint width.</param>
    /// <param name="height">Rotated footprint height.</param>
    /// <returns><see cref="BuildResultCode.Ok"/> or the first rule the placement breaks.</returns>
    BuildResultCode PreviewPlacement(int tileX, int tileY, StructureType type, StructureRotation rotation, out int width, out int height);
}

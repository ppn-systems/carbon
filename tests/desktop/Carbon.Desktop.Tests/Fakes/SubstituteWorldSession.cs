// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using NSubstitute;

namespace Carbon.Desktop.Tests.Fakes;

/// <summary>
/// An <see cref="IWorldSession"/> that forwards every member to an NSubstitute substitute (<see cref="Inner"/>: configure
/// and assert on it as usual), except <c>CopyInventorySlots</c>: NSubstitute cannot intercept a <c>Span</c> parameter, so
/// that one serves <see cref="Inventory"/>. Lets whole-scene tests drive the world scene headless. A member added to the
/// session interfaces fails to compile here until it is forwarded too.
/// </summary>
internal sealed class SubstituteWorldSession : IWorldSession
{
    /// <summary>Gets the substitute every member except the inventory copy forwards to.</summary>
    public IWorldSession Inner { get; } = Substitute.For<IWorldSession>();

    /// <summary>Gets the inventory slots <c>CopyInventorySlots</c> serves.</summary>
    public ItemSlot[] Inventory { get; } = new ItemSlot[PlayerInventoryLayout.TotalCapacity];

    global::System.Boolean global::Carbon.Desktop.Abstractions.Identity.IAuthenticationGateway.IsAuthenticated => this.Inner.IsAuthenticated;

    global::System.Threading.Tasks.Task<global::Carbon.Desktop.Abstractions.Identity.AuthenticationResult> global::Carbon.Desktop.Abstractions.Identity.IAuthenticationGateway.LoginAsync(global::System.String host, global::System.UInt16 tcpPort, global::System.String username, global::System.String password, global::System.Threading.CancellationToken ct) => this.Inner.LoginAsync(host, tcpPort, username, password, ct);

    global::System.Threading.Tasks.Task<global::Carbon.Desktop.Abstractions.Identity.AuthenticationResult> global::Carbon.Desktop.Abstractions.Identity.IAuthenticationGateway.RegisterAsync(global::System.String host, global::System.UInt16 tcpPort, global::System.String username, global::System.String password, global::System.Threading.CancellationToken ct) => this.Inner.RegisterAsync(host, tcpPort, username, password, ct);

    global::System.Threading.Tasks.Task<global::Carbon.Desktop.Abstractions.Identity.AuthenticationResult> global::Carbon.Desktop.Abstractions.Identity.IAuthenticationGateway.LoginWithTokenAsync(global::System.String host, global::System.UInt16 tcpPort, global::Nalix.Abstractions.Primitives.Bytes32 token, global::System.Threading.CancellationToken ct) => this.Inner.LoginWithTokenAsync(host, tcpPort, token, ct);

    global::System.Threading.Tasks.Task<global::Carbon.Desktop.Abstractions.Identity.RememberTokenGrant?> global::Carbon.Desktop.Abstractions.Identity.IAuthenticationGateway.RequestRememberTokenAsync(global::System.Threading.CancellationToken ct) => this.Inner.RequestRememberTokenAsync(ct);

    global::System.Threading.Tasks.Task global::Carbon.Desktop.Abstractions.Identity.IAuthenticationGateway.RevokeRememberTokenAsync(global::System.String host, global::System.UInt16 tcpPort, global::Nalix.Abstractions.Primitives.Bytes32 token, global::System.Threading.CancellationToken ct) => this.Inner.RevokeRememberTokenAsync(host, tcpPort, token, ct);

    global::System.Threading.Tasks.Task global::Carbon.Desktop.Abstractions.Identity.IAuthenticationGateway.LogoutAsync(global::System.Threading.CancellationToken ct) => this.Inner.LogoutAsync(ct);

    global::System.Threading.Tasks.Task global::Carbon.Desktop.Abstractions.Identity.IAuthenticationGateway.InitializeAsync(global::System.Threading.CancellationToken ct) => this.Inner.InitializeAsync(ct);

    global::System.Boolean global::Carbon.Desktop.Abstractions.Session.IConnectionStatus.IsConnected => this.Inner.IsConnected;

    global::System.Boolean global::Carbon.Desktop.Abstractions.Session.IConnectionStatus.IsUdpActive => this.Inner.IsUdpActive;

    global::System.String global::Carbon.Desktop.Abstractions.Session.IConnectionStatus.ConnectionLabel => this.Inner.ConnectionLabel;

    global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::Carbon.Desktop.Abstractions.Identity.CharacterRosterEntry>> global::Carbon.Desktop.Abstractions.Identity.ICharacterRosterService.ListCharactersAsync(global::System.Threading.CancellationToken ct) => this.Inner.ListCharactersAsync(ct);

    global::System.Threading.Tasks.Task<(global::System.Boolean, global::System.String)> global::Carbon.Desktop.Abstractions.Identity.ICharacterRosterService.CreateCharacterAsync(global::System.String characterName, global::Carbon.Contracts.Identity.CharacterLook look, global::System.Threading.CancellationToken ct) => this.Inner.CreateCharacterAsync(characterName, look, ct);

    global::System.Threading.Tasks.Task<(global::System.Boolean, global::System.String)> global::Carbon.Desktop.Abstractions.Identity.ICharacterRosterService.EnterWorldAsync(global::System.Guid characterId, global::System.Threading.CancellationToken ct) => this.Inner.EnterWorldAsync(characterId, ct);

    global::System.String global::Carbon.Desktop.Abstractions.Identity.ICharacterProfile.ActiveCharacterName => this.Inner.ActiveCharacterName;

    global::Carbon.Contracts.Identity.CharacterLook global::Carbon.Desktop.Abstractions.Identity.ICharacterProfile.LocalPlayerLook => this.Inner.LocalPlayerLook;
    global::Carbon.Desktop.Abstractions.Configuration.SyncedRaceCatalog global::Carbon.Desktop.Abstractions.Identity.ICharacterProfile.Races => this.Inner.Races;

    global::Carbon.Desktop.Abstractions.Configuration.SyncedItemCatalog global::Carbon.Desktop.Abstractions.World.IWorldReadModel.Items => this.Inner.Items;

    global::Carbon.Maps.Streaming.ChunkWorld2D global::Carbon.Desktop.Abstractions.World.IWorldReadModel.ChunkWorld => this.Inner.ChunkWorld;

    global::Carbon.Simulation.World.WorldSimulation global::Carbon.Desktop.Abstractions.World.IWorldReadModel.Simulation => this.Inner.Simulation;

    global::Carbon.Simulation.ECS.Entity global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayer => this.Inner.LocalPlayer;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.BuildReachTiles => this.Inner.BuildReachTiles;

    global::System.Numerics.Vector2 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerInterpolatedPosition => this.Inner.LocalPlayerInterpolatedPosition;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerHealth => this.Inner.LocalPlayerHealth;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerMaxHealth => this.Inner.LocalPlayerMaxHealth;

    global::System.UInt16 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerLevel => this.Inner.LocalPlayerLevel;

    global::System.UInt32 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerExperience => this.Inner.LocalPlayerExperience;

    global::System.UInt32 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerXpToNext => this.Inner.LocalPlayerXpToNext;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerMana => this.Inner.LocalPlayerMana;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerMaxMana => this.Inner.LocalPlayerMaxMana;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerStamina => this.Inner.LocalPlayerStamina;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerMaxStamina => this.Inner.LocalPlayerMaxStamina;

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerStaminaExhausted => this.Inner.LocalPlayerStaminaExhausted;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerStaminaFlash => this.Inner.LocalPlayerStaminaFlash;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerDefense => this.Inner.LocalPlayerDefense;

    global::System.Int64 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerGold => this.Inner.LocalPlayerGold;

    global::System.Int64 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerDiamonds => this.Inner.LocalPlayerDiamonds;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerFood => this.Inner.LocalPlayerFood;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerMaxFood => this.Inner.LocalPlayerMaxFood;

    global::System.Byte global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerHungerState => this.Inner.LocalPlayerHungerState;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerMalnutrition => this.Inner.LocalPlayerMalnutrition;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerMood => this.Inner.LocalPlayerMood;

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerFoodPoisoned => this.Inner.LocalPlayerFoodPoisoned;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerEatProgress => this.Inner.LocalPlayerEatProgress;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerRest => this.Inner.LocalPlayerRest;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerMaxRest => this.Inner.LocalPlayerMaxRest;

    global::System.Byte global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerRestState => this.Inner.LocalPlayerRestState;

    global::Carbon.Contracts.Entities.RestMode global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerRestMode => this.Inner.LocalPlayerRestMode;

    global::Carbon.Contracts.Identity.HeroArchetypeClass global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalPlayerClass => this.Inner.LocalPlayerClass;

    global::Carbon.Contracts.Common.CharacterTraits global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalCharacterTraits => this.Inner.LocalCharacterTraits;

    global::Carbon.Contracts.Entities.CharacterSkillMask global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalUnlockedSkillMask => this.Inner.LocalUnlockedSkillMask;

    global::System.UInt16 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalUnspentStatPoints => this.Inner.LocalUnspentStatPoints;

    global::System.UInt16 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalUnspentSkillPoints => this.Inner.LocalUnspentSkillPoints;

    global::System.Byte global::Carbon.Desktop.Abstractions.World.IWorldReadModel.SelectedHotbarSlot => this.Inner.SelectedHotbarSlot;

    global::Carbon.Items.Containers.InventoryContainer global::Carbon.Desktop.Abstractions.World.IWorldReadModel.Inventory => this.Inner.Inventory;

    global::System.Numerics.Vector2 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LastAttackDirection => this.Inner.LastAttackDirection;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.AttackSwingTimer => this.Inner.AttackSwingTimer;

    global::System.Numerics.Vector2 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalFacingDirection => this.Inner.LocalFacingDirection;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalHitFlashTimer => this.Inner.LocalHitFlashTimer;

    global::Carbon.Desktop.Abstractions.World.ActionState global::Carbon.Desktop.Abstractions.World.IWorldReadModel.CurrentAction => this.Inner.CurrentAction;

    global::System.Collections.Generic.IReadOnlyList<global::Carbon.Desktop.Abstractions.World.RemoteEntityView> global::Carbon.Desktop.Abstractions.World.IWorldReadModel.RemoteEntities => this.Inner.RemoteEntities;

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IWorldReadModel.IsReady => this.Inner.IsReady;

    global::System.UInt64 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.WorldSeed => this.Inner.WorldSeed;

    global::System.Single? global::Carbon.Desktop.Abstractions.World.IWorldReadModel.AuthoritativeTimeHour => this.Inner.AuthoritativeTimeHour;

    global::Carbon.Desktop.Abstractions.World.DeathView? global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalDeath => this.Inner.LocalDeath;

    global::Carbon.Desktop.Abstractions.World.WaterView global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalWater => this.Inner.LocalWater;

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IWorldReadModel.IsRespawnPending => this.Inner.IsRespawnPending;

    global::System.Boolean? global::Carbon.Desktop.Abstractions.World.IWorldReadModel.AuthoritativeIsNight => this.Inner.AuthoritativeIsNight;

    global::System.UInt64 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LockedTargetNetworkId => this.Inner.LockedTargetNetworkId;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.ForgeFeedbackTimer => this.Inner.ForgeFeedbackTimer;

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LastForgeSucceeded => this.Inner.LastForgeSucceeded;

    global::Carbon.Contracts.Common.EnhancementOutcome global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LastForgeOutcome => this.Inner.LastForgeOutcome;

    global::System.Byte global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LastForgeNewLevel => this.Inner.LastForgeNewLevel;

    global::System.UInt32 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.ForgeResultSerial => this.Inner.ForgeResultSerial;

    global::System.Collections.Generic.IReadOnlyList<global::Carbon.Desktop.Abstractions.StatusEffects.LocalStatusEffect> global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalStatusEffects => this.Inner.LocalStatusEffects;

    global::Carbon.Combat.Statuses.Config.StatusEffectCatalog global::Carbon.Desktop.Abstractions.World.IWorldReadModel.StatusEffectDefinitions => this.Inner.StatusEffectDefinitions;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.LocalDamageVignetteTimer => this.Inner.LocalDamageVignetteTimer;

    global::System.Single global::Carbon.Desktop.Abstractions.World.IWorldReadModel.GetSkillCooldown(global::System.UInt16 skillId) => this.Inner.GetSkillCooldown(skillId);

    global::System.UInt16 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.GetAttributePoints(global::Carbon.Contracts.Entities.PrimaryAttributeId attribute) => this.Inner.GetAttributePoints(attribute);

    global::System.Byte global::Carbon.Desktop.Abstractions.World.IWorldReadModel.GetSkillRank(global::System.UInt16 skillId) => this.Inner.GetSkillRank(skillId);

    global::System.String? global::Carbon.Desktop.Abstractions.World.IWorldReadModel.ResolveMonsterNameKey(global::System.UInt16 archetypeId) => this.Inner.ResolveMonsterNameKey(archetypeId);

    void global::Carbon.Desktop.Abstractions.World.IWorldReadModel.CopyInventorySlots(global::System.Span<global::Carbon.Items.Containers.ItemSlot> destination) =>
        this.Inventory.AsSpan(0, global::System.Math.Min(this.Inventory.Length, destination.Length)).CopyTo(destination);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IWorldReadModel.IsNearForgeStation(global::System.Single maxDistance) => this.Inner.IsNearForgeStation(maxDistance);

    global::System.Int32 global::Carbon.Desktop.Abstractions.World.IWorldReadModel.GetRepairStationTier(global::Carbon.Contracts.Common.RepairVenue venue) => this.Inner.GetRepairStationTier(venue);

    void global::Carbon.Desktop.Abstractions.World.IWorldReadModel.DrainCombatEvents(global::System.Collections.Generic.List<global::Carbon.Desktop.Abstractions.Events.CombatFeedbackEvent> destination) => this.Inner.DrainCombatEvents(destination);

    void global::Carbon.Desktop.Abstractions.World.IWorldReadModel.DrainPropImpactEvents(global::System.Collections.Generic.List<global::Carbon.Desktop.Abstractions.Events.PropImpactEvent> destination) => this.Inner.DrainPropImpactEvents(destination);

    void global::Carbon.Desktop.Abstractions.World.IWorldReadModel.DrainItemBreakEvents(global::System.Collections.Generic.List<global::Carbon.Desktop.Abstractions.Events.ItemBreakEvent> destination) => this.Inner.DrainItemBreakEvents(destination);

    void global::Carbon.Desktop.Abstractions.World.IWorldReadModel.DrainDirtyChunks(global::System.Collections.Generic.List<(global::System.Int32, global::System.Int32)> destination) => this.Inner.DrainDirtyChunks(destination);

    void global::Carbon.Desktop.Abstractions.World.IWorldReadModel.DrainHudNotifications(global::System.Collections.Generic.List<global::System.String> destination) => this.Inner.DrainHudNotifications(destination);

    void global::Carbon.Desktop.Abstractions.World.IWorldReadModel.CopyConstructionMarkers(global::System.Collections.Generic.List<global::Carbon.Desktop.Abstractions.World.ConstructionMarker> destination) => this.Inner.CopyConstructionMarkers(destination);

    global::Carbon.Maps.Building.BuildResultCode global::Carbon.Desktop.Abstractions.World.IWorldReadModel.PreviewPlacement(global::System.Int32 tileX, global::System.Int32 tileY, global::Carbon.Maps.Building.StructureType type, global::Carbon.Maps.Building.StructureRotation rotation, out global::System.Int32 width, out global::System.Int32 height) => this.Inner.PreviewPlacement(tileX, tileY, type, rotation, out width, out height);

    global::Carbon.Items.Catalog.NpcCapabilities global::Carbon.Desktop.Abstractions.World.INpcCapabilityLookup.GetNpcCapabilities(global::System.UInt16 archetypeId) => this.Inner.GetNpcCapabilities(archetypeId);

    global::Carbon.Desktop.Abstractions.World.IRepairSession global::Carbon.Desktop.Abstractions.World.IGameplayController.Repair => this.Inner.Repair;

    global::Carbon.Desktop.Abstractions.World.ITraderSession global::Carbon.Desktop.Abstractions.World.IGameplayController.Trading => this.Inner.Trading;

    void global::Carbon.Desktop.Abstractions.World.IGameplayController.Update(global::System.Double dt, global::System.Numerics.Vector2 moveDir, global::System.Boolean sprint) => this.Inner.Update(dt, moveDir, sprint);

    void global::Carbon.Desktop.Abstractions.World.IGameplayController.SelectHotbarSlot(global::System.Byte slotIndex) => this.Inner.SelectHotbarSlot(slotIndex);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TriggerPrimaryAction(global::System.Numerics.Vector2 aimWorldPosition, out (global::System.Int32, global::System.Int32) invalidatedChunk) => this.Inner.TriggerPrimaryAction(aimWorldPosition, out invalidatedChunk);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryCraftRecipe(global::System.UInt16 recipeId, global::System.UInt16 quantity) => this.Inner.TryCraftRecipe(recipeId, quantity);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryHarvestNearbyProp(out (global::System.Int32, global::System.Int32) modifiedChunk) => this.Inner.TryHarvestNearbyProp(out modifiedChunk);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryPlaceStarterWall(out (global::System.Int32, global::System.Int32) centerChunk) => this.Inner.TryPlaceStarterWall(out centerChunk);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryToggleDoor(global::System.Int32 tileX, global::System.Int32 tileY) => this.Inner.TryToggleDoor(tileX, tileY);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryDemolishStructure(global::System.Numerics.Vector2 aimWorldPosition, out (global::System.Int32, global::System.Int32) modifiedChunk) => this.Inner.TryDemolishStructure(aimWorldPosition, out modifiedChunk);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryCastSkill(global::System.UInt16 skillId, global::System.Numerics.Vector2 aimWorldPosition) => this.Inner.TryCastSkill(skillId, aimWorldPosition);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryMoveInventorySlot(global::System.UInt16 fromSlot, global::System.UInt16 toSlot) => this.Inner.TryMoveInventorySlot(fromSlot, toSlot);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryQuickMoveInventorySlot(global::System.UInt16 slotIndex) => this.Inner.TryQuickMoveInventorySlot(slotIndex);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryUpgradeInventoryItem(global::System.UInt16 slotIndex, global::System.UInt16 protectionItemId) => this.Inner.TryUpgradeInventoryItem(slotIndex, protectionItemId);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryDropInventoryItem(global::System.UInt16 slotIndex, global::System.Int32 quantity) => this.Inner.TryDropInventoryItem(slotIndex, quantity);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TrySplitInventorySlot(global::System.UInt16 fromSlot, global::System.Int32 quantity) => this.Inner.TrySplitInventorySlot(fromSlot, quantity);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TrySortInventory() => this.Inner.TrySortInventory();

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryDestroyInventoryItem(global::System.UInt16 slotIndex, global::System.Int32 quantity) => this.Inner.TryDestroyInventoryItem(slotIndex, quantity);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryPickupLoot(global::System.UInt64 lootNetworkId) => this.Inner.TryPickupLoot(lootNetworkId);

    void global::Carbon.Desktop.Abstractions.World.IGameplayController.SetLockedTarget(global::System.UInt64 networkId) => this.Inner.SetLockedTarget(networkId);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.CycleNearestHostileTarget(global::System.Single maxRange) => this.Inner.CycleNearestHostileTarget(maxRange);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryAllocateCharacterPoint(global::Carbon.Contracts.Entities.PointAllocationKind kind, global::System.UInt16 targetId, global::System.Byte amount) => this.Inner.TryAllocateCharacterPoint(kind, targetId, amount);

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryToggleRest() => this.Inner.TryToggleRest();

    global::System.Boolean global::Carbon.Desktop.Abstractions.World.IGameplayController.TryRequestRespawn() => this.Inner.TryRequestRespawn();

    global::Carbon.Configuration.Catalog.ConfigCatalog? global::Carbon.Desktop.Abstractions.Configuration.IGameConfigSource.GameConfig => this.Inner.GameConfig;

    event global::System.Action<global::Carbon.Configuration.Catalog.ConfigCatalog>? global::Carbon.Desktop.Abstractions.Configuration.IGameConfigSource.GameConfigChanged { add => this.Inner.GameConfigChanged += value; remove => this.Inner.GameConfigChanged -= value; }

    global::System.Threading.Tasks.Task<global::Carbon.Configuration.Catalog.ConfigCatalog> global::Carbon.Desktop.Abstractions.Configuration.IGameConfigSource.WaitForGameConfigAsync(global::System.Threading.CancellationToken ct) => this.Inner.WaitForGameConfigAsync(ct);

    global::System.Threading.Tasks.ValueTask global::System.IAsyncDisposable.DisposeAsync() => this.Inner.DisposeAsync();
}

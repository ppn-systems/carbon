// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Configuration.Catalog;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Building;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.Desktop.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IWorldSession"/> for client input tests: serves a settable inventory and records every
/// inventory command it receives, in order. Members input code has no business touching throw, so a test fails
/// loudly if a controller starts reaching into networking, rendering state or the simulation.
/// </summary>
internal sealed class RecordingWorldSession : IWorldSession
{
    private readonly ItemSlot[] _slots = new ItemSlot[PlayerInventoryLayout.TotalCapacity];

    /// <summary>Gets the inventory commands sent so far, formatted as <c>Name(args)</c>.</summary>
    public List<string> Commands { get; } = [];

    /// <summary>Gets or sets the value every inventory command returns (the server-side prediction verdict).</summary>
    public bool CommandResult { get; set; } = true;

    public SyncedItemCatalog Items { get; } = new();

    public ItemSlot this[int slot]
    {
        get => _slots[slot];
        set => _slots[slot] = value;
    }

    public void CopyInventorySlots(Span<ItemSlot> destination) => _slots.AsSpan(0, Math.Min(_slots.Length, destination.Length)).CopyTo(destination);

    public bool TryMoveInventorySlot(ushort fromSlot, ushort toSlot) => this.Record($"Move({fromSlot},{toSlot})");

    public bool TryQuickMoveInventorySlot(ushort slotIndex) => this.Record($"QuickMove({slotIndex})");

    public bool TryDropInventoryItem(ushort slotIndex, int quantity) => this.Record($"Drop({slotIndex},{quantity})");

    public bool TrySplitInventorySlot(ushort fromSlot, int quantity) => this.Record($"Split({fromSlot},{quantity})");

    public bool TrySortInventory() => this.Record("Sort()");

    public bool TryDestroyInventoryItem(ushort slotIndex, int quantity) => this.Record($"Destroy({slotIndex},{quantity})");

    public bool TryUpgradeInventoryItem(ushort slotIndex, ushort protectionItemId = 0) => this.Record($"Upgrade({slotIndex},{protectionItemId})");

    private bool Record(string command)
    {
        this.Commands.Add(command);
        return this.CommandResult;
    }

    private static T Unused<T>() => throw NotUsed();

    private static NotSupportedException NotUsed() => new("inventory input must not use this session member");

    // ---- Everything below is outside inventory input and deliberately unsupported. ----
    public bool IsConnected => Unused<bool>();
    public bool IsUdpActive => Unused<bool>();
    public string TransportLabel => Unused<string>();
    public string ActiveCharacterName => Unused<string>();
    public byte LocalPlayerAppearancePreset => Unused<byte>();
    public bool IsAuthenticated => Unused<bool>();
    public ChunkWorld2D ChunkWorld => Unused<ChunkWorld2D>();
    public WorldSimulation Simulation => Unused<WorldSimulation>();
    public Entity LocalPlayer => Unused<Entity>();
    public Vector2 LocalPlayerInterpolatedPosition => Unused<Vector2>();
    public float LocalPlayerHealth => Unused<float>();
    public float LocalPlayerMaxHealth => Unused<float>();
    public ushort LocalPlayerLevel => Unused<ushort>();
    public uint LocalPlayerExperience => Unused<uint>();
    public uint LocalPlayerXpToNext => Unused<uint>();
    public float LocalPlayerMana => Unused<float>();
    public float LocalPlayerMaxMana => Unused<float>();
    public float LocalPlayerStamina => Unused<float>();
    public float LocalPlayerMaxStamina => Unused<float>();
    public float LocalPlayerDefense => Unused<float>();
    public long LocalPlayerGold => Unused<long>();
    public long LocalPlayerDiamonds => Unused<long>();
    public float LocalPlayerFood => Unused<float>();
    public float LocalPlayerMaxFood => Unused<float>();
    public byte LocalPlayerHungerState => Unused<byte>();
    public float LocalPlayerMalnutrition => Unused<float>();
    public float LocalPlayerMood => Unused<float>();
    public bool LocalPlayerFoodPoisoned => Unused<bool>();
    public float LocalPlayerEatProgress => Unused<float>();
    public float LocalPlayerRest => Unused<float>();
    public float LocalPlayerMaxRest => Unused<float>();
    public byte LocalPlayerRestState => Unused<byte>();
    public RestMode LocalPlayerRestMode => Unused<RestMode>();
    public HeroArchetypeClass LocalPlayerClass => Unused<HeroArchetypeClass>();
    public CharacterTraits LocalCharacterTraits => Unused<CharacterTraits>();
    public CharacterSkillMask LocalUnlockedSkillMask => Unused<CharacterSkillMask>();
    public ushort LocalUnspentStatPoints => Unused<ushort>();
    public ushort LocalUnspentSkillPoints => Unused<ushort>();
    public byte SelectedHotbarSlot => Unused<byte>();
    public InventoryContainer Inventory => Unused<InventoryContainer>();
    public Vector2 LastAttackDirection => Unused<Vector2>();
    public float AttackSwingTimer => Unused<float>();
    public Vector2 LocalFacingDirection => Unused<Vector2>();
    public float LocalHitFlashTimer => Unused<float>();
    public ActionState CurrentAction => Unused<ActionState>();
    public IReadOnlyList<RemoteEntityView> RemoteEntities => Unused<IReadOnlyList<RemoteEntityView>>();
    public bool IsReady => Unused<bool>();
    public ulong WorldSeed => Unused<ulong>();
    public ulong LockedTargetNetworkId => Unused<ulong>();
    public float ForgeFeedbackTimer => Unused<float>();
    public bool LastForgeSucceeded => Unused<bool>();
    public ITraderSession Trading => Unused<ITraderSession>();
    public ConfigCatalog? GameConfig => Unused<ConfigCatalog?>();

    public event Action<ConfigCatalog>? GameConfigChanged
    {
        add => Unused<bool>();
        remove => Unused<bool>();
    }

    public float GetSkillCooldown(ushort skillId) => Unused<float>();
    public ushort GetAttributePoints(PrimaryAttributeId attribute) => Unused<ushort>();
    public byte GetSkillRank(ushort skillId) => Unused<byte>();
    public bool IsNearForgeStation(float maxDistance = 175f) => Unused<bool>();
    public void DrainCombatEvents(List<CombatFeedbackEvent> destination) => Unused<bool>();
    public void DrainPropImpactEvents(List<PropImpactEvent> destination) => Unused<bool>();
    public void DrainDirtyChunks(List<(int ChunkX, int ChunkY)> destination) => Unused<bool>();
    public void DrainHudNotifications(List<string> destination) => Unused<bool>();
    public void CopyConstructionMarkers(List<ConstructionMarker> destination) => Unused<bool>();
    public BuildResultCode PreviewPlacement(int tileX, int tileY, StructureType type, StructureRotation rotation, out int width, out int height) => throw NotUsed();
    public Task<AuthenticationResult> LoginAsync(string host, ushort tcpPort, string username, string password, CancellationToken ct = default) => Unused<Task<AuthenticationResult>>();
    public Task<AuthenticationResult> RegisterAsync(string host, ushort tcpPort, string username, string password, CancellationToken ct = default) => Unused<Task<AuthenticationResult>>();
    public Task LogoutAsync(CancellationToken ct = default) => Unused<Task>();
    public Task InitializeAsync(CancellationToken ct = default) => Unused<Task>();
    public Task<IReadOnlyList<CharacterRosterEntry>> ListCharactersAsync(CancellationToken ct = default) => Unused<Task<IReadOnlyList<CharacterRosterEntry>>>();
    public Task<(bool Succeeded, string Message)> CreateCharacterAsync(string characterName, byte appearancePreset, CancellationToken ct = default) => Unused<Task<(bool, string)>>();
    public Task<(bool Succeeded, string Message)> EnterWorldAsync(Guid characterId, CancellationToken ct = default) => Unused<Task<(bool, string)>>();
    public Task<ConfigCatalog> WaitForGameConfigAsync(CancellationToken ct = default) => Unused<Task<ConfigCatalog>>();
    public void Update(double dt, Vector2 moveDir, bool sprint) => Unused<bool>();
    public void SelectHotbarSlot(byte slotIndex) => Unused<bool>();
    public bool TriggerPrimaryAction(Vector2 aimWorldPosition, out (int ChunkX, int ChunkY) invalidatedChunk) => throw NotUsed();
    public bool TryCraftRecipe(ushort recipeId, ushort quantity = 1) => Unused<bool>();
    public bool TryVendorBuy(ushort itemId, ushort quantity = 1) => Unused<bool>();
    public bool TryVendorSell(ushort slotIndex, ushort quantity = 1) => Unused<bool>();
    public bool TryRepairItem(ushort slotIndex) => Unused<bool>();
    public bool TryHarvestNearbyProp(out (int ChunkX, int ChunkY) modifiedChunk) => throw NotUsed();
    public bool TryPlaceStarterWall(out (int ChunkX, int ChunkY) centerChunk) => throw NotUsed();
    public bool TryToggleNearbyDoor(out (int ChunkX, int ChunkY) modifiedChunk) => throw NotUsed();
    public bool TryCastSkill(ushort skillId, Vector2 aimWorldPosition) => Unused<bool>();
    public bool TryPickupNearbyLoot() => Unused<bool>();
    public void SetLockedTarget(ulong networkId) => Unused<bool>();
    public bool CycleNearestHostileTarget(float maxRange = 340f) => Unused<bool>();
    public bool TryAllocateCharacterPoint(PointAllocationKind kind, ushort targetId, byte amount = 1) => Unused<bool>();
    public bool TryToggleRest() => Unused<bool>();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

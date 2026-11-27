// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Contracts.Entities;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Role-segregated gameplay command dispatch port for movement, combat, harvesting, crafting, and construction (ISP).
/// </summary>
public interface IGameplayController
{
    /// <summary>Advances client simulation, prediction reconciliation, chunk streaming, and interpolation for the frame.</summary>
    void Update(double dt, Vector2 moveDir, bool sprint);

    /// <summary>Selects a hotbar slot (<c>0..8</c>) and updates active tool/weapon harvest tier.</summary>
    void SelectHotbarSlot(byte slotIndex);

    /// <summary>Executes primary attack, structure placement, tool harvest, or consumable action aimed toward <paramref name="aimWorldPosition"/>.</summary>
    bool TriggerPrimaryAction(Vector2 aimWorldPosition, out (int ChunkX, int ChunkY) invalidatedChunk);

    /// <summary>Attempts to craft <paramref name="quantity"/> instances of <paramref name="recipeId"/>.</summary>
    bool TryCraftRecipe(ushort recipeId, ushort quantity = 1);

    /// <summary>Requests buying <paramref name="quantity"/> units of <paramref name="itemId"/> from a nearby vendor (server validates and charges gold).</summary>
    bool TryVendorBuy(ushort itemId, ushort quantity = 1);

    /// <summary>Requests selling <paramref name="quantity"/> units from bag slot <paramref name="slotIndex"/> to a nearby vendor.</summary>
    bool TryVendorSell(ushort slotIndex, ushort quantity = 1);

    /// <summary>Requests repairing the item in <paramref name="slotIndex"/> at a forge station for gold.</summary>
    bool TryRepairItem(ushort slotIndex);

    /// <summary>Gets trading with village traders (ADR-025).</summary>
    ITraderSession Trading { get; }

    /// <summary>Attempts to harvest or damage the nearest natural prop beside the local player.</summary>
    bool TryHarvestNearbyProp(out (int ChunkX, int ChunkY) modifiedChunk);

    /// <summary>Places a starter structure near the player's current position.</summary>
    bool TryPlaceStarterWall(out (int ChunkX, int ChunkY) centerChunk);

    /// <summary>Toggles the nearest door beside the local player.</summary>
    bool TryToggleNearbyDoor(out (int ChunkX, int ChunkY) modifiedChunk);

    /// <summary>Attempts to demolish a placed structure at <paramref name="aimWorldPosition"/>.</summary>
    bool TryDemolishStructure(Vector2 aimWorldPosition, out (int ChunkX, int ChunkY) modifiedChunk)
    {
        modifiedChunk = default;
        return false;
    }

    /// <summary>Casts active skill <paramref name="skillId"/> toward <paramref name="aimWorldPosition"/>.</summary>
    bool TryCastSkill(ushort skillId, Vector2 aimWorldPosition);

    /// <summary>Requests moving, swapping, merging, or equipping between inventory slots (<c>0..41</c>).</summary>
    bool TryMoveInventorySlot(ushort fromSlot, ushort toSlot);

    /// <summary>Right-click quick-equips armor or shuttles an item between backpack and hotbar.</summary>
    bool TryQuickMoveInventorySlot(ushort slotIndex);

    /// <summary>
    /// Requests forge enhancement (<c>+1..+10</c>) on the item in <paramref name="slotIndex"/>, spending the protection
    /// scroll <paramref name="protectionItemId"/> when non-zero. The server rolls the outcome (ADR-024).
    /// </summary>
    bool TryUpgradeInventoryItem(ushort slotIndex, ushort protectionItemId = 0);

    /// <summary>Drops <paramref name="quantity"/> units from <paramref name="slotIndex"/> onto the ground.</summary>
    bool TryDropInventoryItem(ushort slotIndex, int quantity);

    /// <summary>
    /// Requests moving <paramref name="quantity"/> units of the bag stack in <paramref name="fromSlot"/> into the first
    /// empty bag slot (ADR-020).
    /// </summary>
    bool TrySplitInventorySlot(ushort fromSlot, int quantity);

    /// <summary>Requests a server-side merge and sort of the backpack (ADR-020).</summary>
    bool TrySortInventory();

    /// <summary>Requests destroying <paramref name="quantity"/> units (<c>&lt;= 0</c> = all) of <paramref name="slotIndex"/> permanently (ADR-020).</summary>
    bool TryDestroyInventoryItem(ushort slotIndex, int quantity);

    /// <summary>Attempts to pick up the closest ground loot item within range.</summary>
    bool TryPickupNearbyLoot();

    /// <summary>Sets or clears the locked target network ID (<c>0</c> clears lock).</summary>
    void SetLockedTarget(ulong networkId);

    /// <summary>Selects or cycles the nearest alive hostile monster within <paramref name="maxRange"/> world units.</summary>
    bool CycleNearestHostileTarget(float maxRange = 340f);

    /// <summary>Allocates unspent attribute points or unlocks/ranks up an active skill.</summary>
    bool TryAllocateCharacterPoint(PointAllocationKind kind, ushort targetId, byte amount = 1);

    /// <summary>
    /// Asks the server to rest (sleep in a nearby bed, otherwise sit) or, when already resting, to stand up (ADR-018).
    /// </summary>
    /// <returns><see langword="true"/> when the request was sent.</returns>
    bool TryToggleRest();

    /// <summary>
    /// Asks the server to respawn the dead local player (ADR-031). Sends at most one request until the server answers.
    /// </summary>
    /// <returns><see langword="true"/> when a request was sent; <see langword="false"/> when alive or already waiting.</returns>
    bool TryRequestRespawn() => false;
}

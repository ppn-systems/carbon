// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Desktop.Abstractions;

/// <summary>
/// Role-segregated gameplay command dispatch port for movement, combat, harvesting, crafting, and construction (ISP).
/// </summary>
public interface IClientGameplayController
{
    /// <summary>Advances client simulation, prediction reconciliation, chunk streaming, and interpolation for the frame.</summary>
    void Update(double dt, Vector2 moveDir, bool sprint);

    /// <summary>Selects a hotbar slot (<c>0..8</c>) and updates active tool/weapon harvest tier.</summary>
    void SelectHotbarSlot(byte slotIndex);

    /// <summary>Executes primary attack, structure placement, tool harvest, or consumable action aimed toward <paramref name="aimWorldPosition"/>.</summary>
    bool TriggerPrimaryAction(Vector2 aimWorldPosition, out (int ChunkX, int ChunkY) invalidatedChunk);

    /// <summary>Attempts to craft one instance of <paramref name="recipeId"/>.</summary>
    bool TryCraftRecipe(ushort recipeId);

    /// <summary>Attempts to harvest or damage the nearest natural prop beside the local player.</summary>
    bool TryHarvestNearbyProp(out (int ChunkX, int ChunkY) modifiedChunk);

    /// <summary>Places a starter structure near the player's current position.</summary>
    bool TryPlaceTestHouse(out (int ChunkX, int ChunkY) centerChunk);

    /// <summary>Toggles the nearest door beside the local player.</summary>
    bool TryToggleNearbyDoor(out (int ChunkX, int ChunkY) modifiedChunk);

    /// <summary>Casts active skill (<c>1..3</c>) toward <paramref name="aimWorldPosition"/>.</summary>
    bool TryCastSkill(byte skillSlot, Vector2 aimWorldPosition);

    /// <summary>Requests moving, swapping, merging, or equipping between inventory slots (<c>0..39</c>).</summary>
    bool TryMoveInventorySlot(ushort fromSlot, ushort toSlot);

    /// <summary>Right-click quick-equips armor or shuttles an item between backpack and hotbar.</summary>
    bool TryQuickMoveInventorySlot(ushort slotIndex);

    /// <summary>Requests forge enhancement (<c>+1..+10</c>) on the item in <paramref name="slotIndex"/>.</summary>
    bool TryUpgradeInventoryItem(ushort slotIndex);

    /// <summary>Drops <paramref name="quantity"/> units from <paramref name="slotIndex"/> onto the ground.</summary>
    bool TryDropInventoryItem(ushort slotIndex, int quantity);

    /// <summary>Attempts to pick up the closest ground loot item within range.</summary>
    bool TryPickupNearbyLoot();

    /// <summary>Sets or clears the locked target network ID (<c>0</c> clears lock).</summary>
    void SetLockedTarget(ulong networkId);

    /// <summary>Selects or cycles the nearest alive hostile monster within <paramref name="maxRange"/> world units.</summary>
    bool CycleNearestHostileTarget(float maxRange = 340f);
}

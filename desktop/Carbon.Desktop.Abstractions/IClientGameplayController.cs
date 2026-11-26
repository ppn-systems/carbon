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
}

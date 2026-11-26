// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Maps.S2C;
using Carbon.Maps.Building;

namespace Carbon.Abstractions.World;

/// <summary>
/// Role-segregated world terrain chunk delta streaming, prop harvesting, and building construction port (ISP).
/// </summary>
public interface IWorldMapService
{
    /// <summary>Gets the world generation seed.</summary>
    ulong WorldSeed { get; }

    /// <summary>Gets the world generator version.</summary>
    ushort GeneratorVersion { get; }

    /// <summary>Gets the tile size in world units.</summary>
    float TileSize { get; }

    /// <summary>Gets the macro terrain noise frequency.</summary>
    float Frequency { get; }

    /// <summary>Gets the default safe starter spawn world position.</summary>
    System.Numerics.Vector2 DefaultSpawnWorldPosition { get; }

    /// <summary>Builds a thread-safe wire delta packet snapshot for the requested chunk when its revision exceeds <paramref name="knownRevision"/>.</summary>
    bool TryCreateChunkDeltaPacket(int chunkX, int chunkY, uint knownRevision, out MapChunkDelta? packet);

    /// <summary>Attempts to harvest or damage the natural prop on the specified world tile.</summary>
    WorldMutationOutcome TryHarvestProp(ulong connectionId, int tileX, int tileY);

    /// <summary>Attempts to construct a structure on the specified world tile using the matching kit.</summary>
    WorldMutationOutcome TryPlaceStructure(ulong connectionId, int tileX, int tileY, StructureType structureType);

    /// <summary>Attempts to demolish an existing constructed structure on the specified world tile.</summary>
    WorldMutationOutcome TryDemolishStructure(ulong connectionId, int tileX, int tileY);

    /// <summary>Attempts to toggle an existing door on the specified world tile.</summary>
    WorldMutationOutcome TryToggleDoor(ulong connectionId, int tileX, int tileY);
}

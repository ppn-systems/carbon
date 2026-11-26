// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading.Tasks;
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

    /// <summary>
    /// Queues a harvest of the natural prop on the tile; completes after the next tick has processed it (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitHarvestPropAsync(ulong connectionId, int tileX, int tileY);

    /// <summary>
    /// Queues placing a construction blueprint anchored on the tile. Materials are taken from the player's inventory
    /// and construction work advances each tick while the player stays in reach (<c>docs/building.md</c>). Completes
    /// after the next tick has processed it (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitPlaceStructureAsync(ulong connectionId, int tileX, int tileY, StructureType structureType, StructureRotation rotation = StructureRotation.North);

    /// <summary>
    /// Queues cancelling the blueprint covering the tile (100% refund), or deconstructing the owned structure there
    /// (work over time, partial refund). Completes after the next tick has processed it (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitDemolishStructureAsync(ulong connectionId, int tileX, int tileY);

    /// <summary>Queues toggling the door on the tile; completes after the next tick has processed it (ADR-017).</summary>
    ValueTask<WorldMutationOutcome> SubmitToggleDoorAsync(ulong connectionId, int tileX, int tileY);
}

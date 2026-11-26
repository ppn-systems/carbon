// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Building;

/// <summary>
/// A blueprint or frame awaiting materials or work. Anchored on its top-left footprint tile and reserving the
/// whole footprint. Persisted in <see cref="Streaming.ChunkDelta.Sites"/> of the anchor tile's chunk.
/// </summary>
public sealed class ConstructionSite
{
    /// <summary>Largest allowed footprint side in tiles (fits the packed nibble encoding).</summary>
    public const int MaxFootprintSide = 15;

    private readonly int[] _delivered;

    /// <summary>Initializes a new instance of the <see cref="ConstructionSite"/> class.</summary>
    /// <param name="type">Structure type being built.</param>
    /// <param name="rotation">Placement rotation.</param>
    /// <param name="ownerId">Player id that placed the blueprint (0 = unowned).</param>
    /// <param name="width">Rotated footprint width.</param>
    /// <param name="height">Rotated footprint height.</param>
    /// <param name="costEntries">Number of material cost entries tracked in <see cref="GetDelivered"/>.</param>
    public ConstructionSite(StructureType type, StructureRotation rotation, ulong ownerId, int width, int height, int costEntries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxFootprintSide);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxFootprintSide);
        ArgumentOutOfRangeException.ThrowIfNegative(costEntries);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(costEntries, byte.MaxValue);

        this.Type = type;
        this.Rotation = rotation;
        this.OwnerId = ownerId;
        this.Width = width;
        this.Height = height;
        this.Stage = ConstructionStage.Blueprint;
        _delivered = costEntries == 0 ? [] : new int[costEntries];
    }

    /// <summary>Gets the structure type being built.</summary>
    public StructureType Type { get; }

    /// <summary>Gets the placement rotation.</summary>
    public StructureRotation Rotation { get; }

    /// <summary>Gets the owning player id (0 = unowned).</summary>
    public ulong OwnerId { get; }

    /// <summary>Gets the rotated footprint width.</summary>
    public int Width { get; }

    /// <summary>Gets the rotated footprint height.</summary>
    public int Height { get; }

    /// <summary>Gets the current stage (<see cref="ConstructionStage.Blueprint"/> or <see cref="ConstructionStage.Frame"/>).</summary>
    public ConstructionStage Stage { get; internal set; }

    /// <summary>Gets the accumulated construction work points.</summary>
    public float WorkDone { get; internal set; }

    /// <summary>Gets the number of tracked material cost entries.</summary>
    public int CostEntryCount => _delivered.Length;

    /// <summary>Gets the delivered quantity of a cost entry, in definition order.</summary>
    /// <param name="costIndex">Cost entry index.</param>
    /// <returns>Delivered units.</returns>
    public int GetDelivered(int costIndex) => _delivered[costIndex];

    internal void SetDelivered(int costIndex, int quantity) => _delivered[costIndex] = quantity;

    /// <summary>Evaluates whether a world tile lies inside the footprint anchored at (<paramref name="anchorX"/>, <paramref name="anchorY"/>).</summary>
    /// <param name="anchorX">Anchor world tile X.</param>
    /// <param name="anchorY">Anchor world tile Y.</param>
    /// <param name="tileX">Tested world tile X.</param>
    /// <param name="tileY">Tested world tile Y.</param>
    /// <returns><c>true</c> when the tile is covered.</returns>
    public bool Covers(int anchorX, int anchorY, int tileX, int tileY) =>
        tileX >= anchorX && tileX < anchorX + this.Width && tileY >= anchorY && tileY < anchorY + this.Height;

    /// <summary>Copies the mutable state of a site with the same shape into this instance without allocating.</summary>
    /// <param name="other">The source site.</param>
    /// <returns><c>false</c> when the shapes differ and nothing was copied.</returns>
    internal bool TryCopyFrom(ConstructionSite other)
    {
        if (other.Type != this.Type || other.Rotation != this.Rotation || other.OwnerId != this.OwnerId ||
            other.Width != this.Width || other.Height != this.Height || other._delivered.Length != _delivered.Length)
        {
            return false;
        }

        this.Stage = other.Stage;
        this.WorkDone = other.WorkDone;
        other._delivered.CopyTo(_delivered, 0);
        return true;
    }

    /// <summary>Creates an independent copy (used for persistence snapshots).</summary>
    /// <returns>The copy.</returns>
    public ConstructionSite Clone()
    {
        ConstructionSite copy = new(this.Type, this.Rotation, this.OwnerId, this.Width, this.Height, _delivered.Length)
        {
            Stage = this.Stage,
            WorkDone = this.WorkDone
        };
        _delivered.CopyTo(copy._delivered, 0);
        return copy;
    }
}

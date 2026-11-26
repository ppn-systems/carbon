// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;

namespace Carbon.Maps.Streaming;

/// <summary>
/// Player-made changes to one chunk on top of its deterministic, seed-generated baseline.
/// A chunk is regenerated from the seed whenever it is (re)loaded, and its delta is then re-applied,
/// so edits survive eviction. Deltas are also the unit persisted to storage and sent over the wire.
/// </summary>
public sealed class ChunkDelta
{
    private readonly Dictionary<int, MapTile> _tiles = new();
    private readonly Dictionary<int, PropDestructibility> _propStates = new();
    private readonly Dictionary<int, ulong> _propRespawnTicks = new();
    private readonly Dictionary<int, StructureTile> _structures = new();
    private readonly Dictionary<int, ConstructionSite> _sites = new();
    private readonly Dictionary<int, StructureRecord> _records = new();
    private readonly ReadOnlyDictionary<int, MapTile> _tilesView;
    private readonly ReadOnlyDictionary<int, PropDestructibility> _propStatesView;
    private readonly ReadOnlyDictionary<int, StructureTile> _structuresView;
    private readonly ReadOnlyDictionary<int, ConstructionSite> _sitesView;
    private readonly ReadOnlyDictionary<int, StructureRecord> _recordsView;

    /// <summary>Initializes a new instance of the <see cref="ChunkDelta"/> class.</summary>
    /// <param name="chunkX">Chunk X coordinate.</param>
    /// <param name="chunkY">Chunk Y coordinate.</param>
    public ChunkDelta(int chunkX, int chunkY)
    {
        this.ChunkX = chunkX;
        this.ChunkY = chunkY;
        _tilesView = _tiles.AsReadOnly();
        _propStatesView = _propStates.AsReadOnly();
        _structuresView = _structures.AsReadOnly();
        _sitesView = _sites.AsReadOnly();
        _recordsView = _records.AsReadOnly();
    }

    /// <summary>Gets the chunk X coordinate.</summary>
    public int ChunkX { get; }

    /// <summary>Gets the chunk Y coordinate.</summary>
    public int ChunkY { get; }

    /// <summary>Gets the packed chunk key.</summary>
    public long Key => Chunk2D.PackKey(this.ChunkX, this.ChunkY);

    /// <summary>
    /// Gets a monotonically increasing revision, bumped on every change. Clients compare it to decide
    /// whether their cached copy of the chunk is stale.
    /// </summary>
    public uint Revision { get; private set; }

    /// <summary>Gets the overridden tiles, keyed by local index (<c>localY * Chunk2D.Size + localX</c>).</summary>
    public ReadOnlyDictionary<int, MapTile> Tiles => _tilesView;

    /// <summary>Gets the overridden prop states, keyed by the local index of the tile the prop is anchored on.</summary>
    public ReadOnlyDictionary<int, PropDestructibility> PropStates => _propStatesView;

    /// <summary>Gets the constructed structures in this chunk, keyed by local tile index (<c>localY * Chunk2D.Size + localX</c>).</summary>
    public ReadOnlyDictionary<int, StructureTile> Structures => _structuresView;

    /// <summary>
    /// Gets the construction sites (blueprints / frames) anchored in this chunk, keyed by the local index of the anchor tile.
    /// Treat the values as read-only snapshots.
    /// </summary>
    public ReadOnlyDictionary<int, ConstructionSite> Sites => _sitesView;

    /// <summary>Gets the ownership / footprint records of completed structure tiles, keyed by local tile index.</summary>
    public ReadOnlyDictionary<int, StructureRecord> StructureRecords => _recordsView;

    /// <summary>Gets a value indicating whether the delta holds no changes.</summary>
    public bool IsEmpty => _tiles.Count == 0 && _propStates.Count == 0 && _structures.Count == 0 && _sites.Count == 0 && _records.Count == 0;

    /// <summary>Converts local tile coordinates into the delta's local index.</summary>
    /// <param name="localX">Local tile X.</param>
    /// <param name="localY">Local tile Y.</param>
    /// <returns>The local index.</returns>
    public static int ToLocalIndex(int localX, int localY) => (localY * Chunk2D.Size) + localX;

    /// <summary>Records a tile override.</summary>
    /// <param name="localX">Local tile X.</param>
    /// <param name="localY">Local tile Y.</param>
    /// <param name="tile">The new tile.</param>
    public void SetTile(int localX, int localY, in MapTile tile)
    {
        _tiles[ToLocalIndex(localX, localY)] = tile;
        this.Revision++;
    }

    /// <summary>Records a prop state override (damage, destruction).</summary>
    /// <param name="localX">Local tile X of the prop anchor.</param>
    /// <param name="localY">Local tile Y of the prop anchor.</param>
    /// <param name="state">The new prop state.</param>
    public void SetPropState(int localX, int localY, PropDestructibility state) =>
        this.SetPropState(localX, localY, state, respawnAtTick: 0);

    /// <summary>Records a prop state override with an optional simulation respawn tick.</summary>
    /// <param name="localX">Local tile X of the prop anchor.</param>
    /// <param name="localY">Local tile Y of the prop anchor.</param>
    /// <param name="state">The new prop state.</param>
    /// <param name="respawnAtTick">World tick after which the destroyed prop state expires (0 = permanent).</param>
    public void SetPropState(int localX, int localY, PropDestructibility state, ulong respawnAtTick)
    {
        int key = ToLocalIndex(localX, localY);
        _propStates[key] = state;
        if (state.IsDestroyed && respawnAtTick > 0)
        {
            _propRespawnTicks[key] = respawnAtTick;
        }
        else
        {
            _ = _propRespawnTicks.Remove(key);
        }

        this.Revision++;
    }

    /// <summary>
    /// Expires destroyed prop states whose scheduled respawn tick has arrived.
    /// </summary>
    /// <param name="currentTick">Current world simulation tick.</param>
    /// <param name="removedLocalIndices">Optional destination populated with local packed tile keys of respawned props.</param>
    /// <returns>The number of props whose state was reset for respawn.</returns>
    internal int RemoveExpiredPropStates(ulong currentTick, List<int>? removedLocalIndices = null)
    {
        if (_propRespawnTicks.Count == 0)
        {
            return 0;
        }

        int removed = 0;
        List<int>? expired = null;
        foreach (KeyValuePair<int, ulong> kvp in _propRespawnTicks)
        {
            if (kvp.Value <= currentTick)
            {
                expired ??= [];
                expired.Add(kvp.Key);
            }
        }

        if (expired is not null)
        {
            for (int i = 0; i < expired.Count; i++)
            {
                int key = expired[i];
                _ = _propRespawnTicks.Remove(key);
                if (_propStates.Remove(key))
                {
                    removed++;
                    removedLocalIndices?.Add(key);
                }
            }

            if (removed > 0)
            {
                this.Revision++;
            }
        }

        return removed;
    }

    /// <summary>Records or updates a constructed structure on a tile within the chunk.</summary>
    /// <param name="localX">Local tile X.</param>
    /// <param name="localY">Local tile Y.</param>
    /// <param name="structure">The constructed structure state.</param>
    public void SetStructure(int localX, int localY, in StructureTile structure)
    {
        _structures[ToLocalIndex(localX, localY)] = structure;
        this.Revision++;
    }

    /// <summary>Removes a constructed structure from the chunk delta (e.g. after demolition).</summary>
    /// <param name="localX">Local tile X.</param>
    /// <param name="localY">Local tile Y.</param>
    public void RemoveStructure(int localX, int localY)
    {
        _ = _structures.Remove(ToLocalIndex(localX, localY));
        this.Revision++;
    }

    /// <summary>Records or updates a construction site anchored on a tile; the delta keeps its own snapshot copy.</summary>
    /// <param name="localX">Local tile X of the anchor.</param>
    /// <param name="localY">Local tile Y of the anchor.</param>
    /// <param name="site">The site state.</param>
    public void SetSite(int localX, int localY, ConstructionSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        int key = ToLocalIndex(localX, localY);
        if (!_sites.TryGetValue(key, out ConstructionSite? existing) || !existing.TryCopyFrom(site))
        {
            _sites[key] = site.Clone();
        }

        this.Revision++;
    }

    /// <summary>Removes a construction site (completed, cancelled).</summary>
    /// <param name="localX">Local tile X of the anchor.</param>
    /// <param name="localY">Local tile Y of the anchor.</param>
    public void RemoveSite(int localX, int localY)
    {
        _ = _sites.Remove(ToLocalIndex(localX, localY));
        this.Revision++;
    }

    /// <summary>Records the ownership / footprint metadata of a completed structure tile.</summary>
    /// <param name="localX">Local tile X.</param>
    /// <param name="localY">Local tile Y.</param>
    /// <param name="record">The record.</param>
    public void SetStructureRecord(int localX, int localY, StructureRecord record)
    {
        _records[ToLocalIndex(localX, localY)] = record;
        this.Revision++;
    }

    /// <summary>Removes the ownership record of a structure tile.</summary>
    /// <param name="localX">Local tile X.</param>
    /// <param name="localY">Local tile Y.</param>
    public void RemoveStructureRecord(int localX, int localY)
    {
        _ = _records.Remove(ToLocalIndex(localX, localY));
        this.Revision++;
    }

    /// <summary>
    /// Refreshes the cached wall connectivity mask of a recorded structure when an adjacent tile changes,
    /// without bumping <see cref="Revision"/>.
    /// </summary>
    /// <param name="localX">Local tile X.</param>
    /// <param name="localY">Local tile Y.</param>
    /// <param name="connectivity">The recomputed wall connectivity mask.</param>
    internal void RefreshStructureConnectivity(int localX, int localY, WallConnectivityMask connectivity)
    {
        int key = ToLocalIndex(localX, localY);
        if (_structures.TryGetValue(key, out StructureTile existing))
        {
            _structures[key] = existing.WithConnectivity(connectivity);
        }
    }

    /// <summary>Overrides the revision, used when importing a persisted or received delta.</summary>
    /// <param name="revision">The stored revision.</param>
    public void RestoreRevision(uint revision) => this.Revision = revision;

    /// <summary>
    /// Gets the generator version the delta was recorded against. Edits only line up with the world they were
    /// made in: see <see cref="MigrateTo"/>.
    /// </summary>
    public ushort GeneratorVersion { get; private set; } = ProceduralWorldGenerator.GeneratorVersion;

    /// <summary>
    /// Returns this delta carried over to another generator version. Tile edits and constructed structures are kept:
    /// they are what players placed or dug and stay meaningful on any terrain. Natural prop states (chopped trees,
    /// mined ores) are dropped: the new generator may put different props on those tiles, so the old states would
    /// damage the wrong objects; resources simply grow back.
    /// </summary>
    /// <param name="generatorVersion">The target version.</param>
    /// <returns>This delta when the version already matches, otherwise a migrated copy.</returns>
    public ChunkDelta MigrateTo(ushort generatorVersion)
    {
        if (generatorVersion == this.GeneratorVersion)
        {
            return this;
        }

        ChunkDelta migrated = new(this.ChunkX, this.ChunkY) { GeneratorVersion = generatorVersion };
        foreach (KeyValuePair<int, MapTile> kvp in _tiles)
        {
            migrated._tiles[kvp.Key] = kvp.Value;
        }

        foreach (KeyValuePair<int, StructureTile> kvp in _structures)
        {
            migrated._structures[kvp.Key] = kvp.Value;
        }

        foreach (KeyValuePair<int, ConstructionSite> kvp in _sites)
        {
            migrated._sites[kvp.Key] = kvp.Value.Clone();
        }

        foreach (KeyValuePair<int, StructureRecord> kvp in _records)
        {
            migrated._records[kvp.Key] = kvp.Value;
        }

        migrated.Revision = this.Revision + 1;
        return migrated;
    }

    // Wire format (little-endian): format, generator version, chunk X/Y, revision, tile edits, prop states, structures (v2+),
    // construction sites and structure records (v3+).
    private const byte LegacyFormatVersion = 1;
    private const byte StructuresFormatVersion = 2;
    private const byte FormatVersion = 3;
    private const int HeaderSize = 1 + 2 + 4 + 4 + 4;
    private const int TileSize = 2 + 1 + 4 + 4 + 4 + 1 + 1 + 1;
    private const int PropSize = 2 + 1 + 4 + 4 + 1;
    private const int StructureSize = 2 + 1 + 1 + 1 + 1 + 1 + 1 + 4 + 4 + 4;
    private const int SiteBaseSize = 2 + 1 + 1 + 1 + 1 + 1 + 8 + 4 + 1;
    private const int RecordSize = 2 + 8 + 1 + 1 + 1 + 1;

    /// <summary>Serializes the delta for storage or the network; identical bytes on every platform.</summary>
    /// <returns>The encoded delta.</returns>
    public byte[] Serialize()
    {
        int siteBytes = 0;
        foreach (ConstructionSite site in _sites.Values)
        {
            siteBytes += SiteBaseSize + (site.CostEntryCount * 4);
        }

        int totalSize = HeaderSize + 2 + (_tiles.Count * TileSize) + 2 + (_propStates.Count * PropSize) + 2 + (_structures.Count * StructureSize) +
            2 + siteBytes + 2 + (_records.Count * RecordSize);
        byte[] buffer = new byte[totalSize];
        Span<byte> span = buffer;
        span[0] = FormatVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(span[1..], this.GeneratorVersion);
        BinaryPrimitives.WriteInt32LittleEndian(span[3..], this.ChunkX);
        BinaryPrimitives.WriteInt32LittleEndian(span[7..], this.ChunkY);
        BinaryPrimitives.WriteUInt32LittleEndian(span[11..], this.Revision);
        int o = HeaderSize;

        // Sorted by tile index, so equal deltas always encode to equal bytes.
        List<int> keys = [.. _tiles.Keys];
        keys.Sort();
        BinaryPrimitives.WriteUInt16LittleEndian(span[o..], (ushort)keys.Count);
        o += 2;
        foreach (int key in keys)
        {
            MapTile t = _tiles[key];
            BinaryPrimitives.WriteUInt16LittleEndian(span[o..], (ushort)key);
            span[o + 2] = (byte)t.Biome;
            BinaryPrimitives.WriteSingleLittleEndian(span[(o + 3)..], t.Elevation);
            BinaryPrimitives.WriteSingleLittleEndian(span[(o + 7)..], t.Temperature);
            BinaryPrimitives.WriteSingleLittleEndian(span[(o + 11)..], t.Moisture);
            span[o + 15] = t.IsSolid ? (byte)1 : (byte)0;
            span[o + 16] = t.Variant;
            span[o + 17] = t.HeightLevel;
            o += TileSize;
        }

        keys = [.. _propStates.Keys];
        keys.Sort();
        BinaryPrimitives.WriteUInt16LittleEndian(span[o..], (ushort)keys.Count);
        o += 2;
        foreach (int key in keys)
        {
            PropDestructibility p = _propStates[key];
            BinaryPrimitives.WriteUInt16LittleEndian(span[o..], (ushort)key);
            span[o + 2] = (byte)((p.IsDestructible ? 1 : 0) | (p.IsDestroyed ? 2 : 0));
            BinaryPrimitives.WriteSingleLittleEndian(span[(o + 3)..], p.MaxDurability);
            BinaryPrimitives.WriteSingleLittleEndian(span[(o + 7)..], p.CurrentDurability);
            span[o + 11] = p.HarvestTier;
            o += PropSize;
        }

        keys = [.. _structures.Keys];
        keys.Sort();
        BinaryPrimitives.WriteUInt16LittleEndian(span[o..], (ushort)keys.Count);
        o += 2;
        foreach (int key in keys)
        {
            StructureTile s = _structures[key];
            BinaryPrimitives.WriteUInt16LittleEndian(span[o..], (ushort)key);
            span[o + 2] = (byte)s.Type;
            span[o + 3] = (byte)s.Material;
            span[o + 4] = (byte)s.Category;
            span[o + 5] = (byte)(
                (s.Destructibility.IsDestructible ? 1 : 0) |
                (s.Destructibility.IsDestroyed ? 2 : 0) |
                (s.HasRoof ? 4 : 0) |
                (s.DoorState == DoorState.Open ? 8 : 0));
            span[o + 6] = s.Destructibility.HarvestTier;
            span[o + 7] = (byte)s.Connectivity;
            BinaryPrimitives.WriteSingleLittleEndian(span[(o + 8)..], s.Destructibility.MaxDurability);
            BinaryPrimitives.WriteSingleLittleEndian(span[(o + 12)..], s.Destructibility.CurrentDurability);
            BinaryPrimitives.WriteSingleLittleEndian(span[(o + 16)..], s.MoveSpeedMultiplier);
            o += StructureSize;
        }

        keys = [.. _sites.Keys];
        keys.Sort();
        BinaryPrimitives.WriteUInt16LittleEndian(span[o..], (ushort)keys.Count);
        o += 2;
        foreach (int key in keys)
        {
            ConstructionSite site = _sites[key];
            BinaryPrimitives.WriteUInt16LittleEndian(span[o..], (ushort)key);
            span[o + 2] = (byte)site.Type;
            span[o + 3] = (byte)site.Rotation;
            span[o + 4] = (byte)site.Stage;
            span[o + 5] = (byte)site.Width;
            span[o + 6] = (byte)site.Height;
            BinaryPrimitives.WriteUInt64LittleEndian(span[(o + 7)..], site.OwnerId);
            BinaryPrimitives.WriteSingleLittleEndian(span[(o + 15)..], site.WorkDone);
            span[o + 19] = (byte)site.CostEntryCount;
            o += SiteBaseSize;
            for (int i = 0; i < site.CostEntryCount; i++, o += 4)
            {
                BinaryPrimitives.WriteInt32LittleEndian(span[o..], site.GetDelivered(i));
            }
        }

        keys = [.. _records.Keys];
        keys.Sort();
        BinaryPrimitives.WriteUInt16LittleEndian(span[o..], (ushort)keys.Count);
        o += 2;
        foreach (int key in keys)
        {
            StructureRecord r = _records[key];
            BinaryPrimitives.WriteUInt16LittleEndian(span[o..], (ushort)key);
            BinaryPrimitives.WriteUInt64LittleEndian(span[(o + 2)..], r.OwnerId);
            span[o + 10] = r.AnchorOffsetX;
            span[o + 11] = r.AnchorOffsetY;
            span[o + 12] = (byte)r.Rotation;
            span[o + 13] = (byte)r.FloorBeneath;
            o += RecordSize;
        }

        return buffer;
    }

    /// <summary>Decodes a delta written by <see cref="Serialize"/>.</summary>
    /// <param name="data">The encoded delta.</param>
    /// <returns>The delta, at the generator version it was recorded with.</returns>
    /// <exception cref="FormatException">The data is truncated, has an unknown format or bad tile indices.</exception>
    public static ChunkDelta Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize + 4 || data[0] is not (LegacyFormatVersion or StructuresFormatVersion or FormatVersion))
        {
            throw new FormatException("Unknown or truncated chunk delta.");
        }

        byte format = data[0];
        ChunkDelta delta = new(BinaryPrimitives.ReadInt32LittleEndian(data[3..]), BinaryPrimitives.ReadInt32LittleEndian(data[7..]))
        {
            GeneratorVersion = BinaryPrimitives.ReadUInt16LittleEndian(data[1..]),
        };
        uint revision = BinaryPrimitives.ReadUInt32LittleEndian(data[11..]);
        int o = HeaderSize;

        int tiles = BinaryPrimitives.ReadUInt16LittleEndian(data[o..]);
        o += 2;
        Require(data, o + (tiles * TileSize) + 2);
        for (int i = 0; i < tiles; i++, o += TileSize)
        {
            int key = ReadIndex(data[o..]);
            delta._tiles[key] = new MapTile(
                (BiomeType)data[o + 2],
                BinaryPrimitives.ReadSingleLittleEndian(data[(o + 3)..]),
                BinaryPrimitives.ReadSingleLittleEndian(data[(o + 7)..]),
                BinaryPrimitives.ReadSingleLittleEndian(data[(o + 11)..]),
                data[o + 15] != 0,
                data[o + 16],
                data[o + 17]);
        }

        int props = BinaryPrimitives.ReadUInt16LittleEndian(data[o..]);
        o += 2;
        Require(data, o + (props * PropSize));
        for (int i = 0; i < props; i++, o += PropSize)
        {
            int key = ReadIndex(data[o..]);
            byte flags = data[o + 2];
            delta._propStates[key] = new PropDestructibility(
                (flags & 1) != 0,
                BinaryPrimitives.ReadSingleLittleEndian(data[(o + 3)..]),
                BinaryPrimitives.ReadSingleLittleEndian(data[(o + 7)..]),
                data[o + 11],
                (flags & 2) != 0);
        }

        if (format >= StructuresFormatVersion)
        {
            Require(data, o + 2);
            int structures = BinaryPrimitives.ReadUInt16LittleEndian(data[o..]);
            o += 2;
            Require(data, o + (structures * StructureSize));
            for (int i = 0; i < structures; i++, o += StructureSize)
            {
                int key = ReadIndex(data[o..]);
                byte flags = data[o + 5];
                PropDestructibility destructibility = new(
                    (flags & 1) != 0,
                    BinaryPrimitives.ReadSingleLittleEndian(data[(o + 8)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(data[(o + 12)..]),
                    data[o + 6],
                    (flags & 2) != 0);
                delta._structures[key] = new StructureTile(
                    (StructureType)data[o + 2],
                    (StructureMaterial)data[o + 3],
                    (StructureCategory)data[o + 4],
                    destructibility,
                    (flags & 8) != 0 ? DoorState.Open : DoorState.Closed,
                    (WallConnectivityMask)data[o + 7],
                    (flags & 4) != 0,
                    BinaryPrimitives.ReadSingleLittleEndian(data[(o + 16)..]));
            }
        }

        if (format >= FormatVersion)
        {
            DeserializeConstruction(delta, data, o);
        }

        delta.Revision = revision;
        return delta;
    }

    private static void DeserializeConstruction(ChunkDelta delta, ReadOnlySpan<byte> data, int o)
    {
        Require(data, o + 2);
        int sites = BinaryPrimitives.ReadUInt16LittleEndian(data[o..]);
        o += 2;
        for (int i = 0; i < sites; i++)
        {
            Require(data, o + SiteBaseSize);
            int key = ReadIndex(data[o..]);
            int width = data[o + 5];
            int height = data[o + 6];
            int costEntries = data[o + 19];
            if (width is < 1 or > ConstructionSite.MaxFootprintSide || height is < 1 or > ConstructionSite.MaxFootprintSide ||
                data[o + 4] is not ((byte)ConstructionStage.Blueprint or (byte)ConstructionStage.Frame))
            {
                throw new FormatException("Chunk delta construction site is malformed.");
            }

            ConstructionSite site = new(
                (StructureType)data[o + 2],
                (StructureRotation)data[o + 3],
                BinaryPrimitives.ReadUInt64LittleEndian(data[(o + 7)..]),
                width,
                height,
                costEntries)
            {
                Stage = (ConstructionStage)data[o + 4],
                WorkDone = BinaryPrimitives.ReadSingleLittleEndian(data[(o + 15)..])
            };
            o += SiteBaseSize;
            Require(data, o + (costEntries * 4));
            for (int c = 0; c < costEntries; c++, o += 4)
            {
                site.SetDelivered(c, BinaryPrimitives.ReadInt32LittleEndian(data[o..]));
            }

            delta._sites[key] = site;
        }

        Require(data, o + 2);
        int records = BinaryPrimitives.ReadUInt16LittleEndian(data[o..]);
        o += 2;
        Require(data, o + (records * RecordSize));
        for (int i = 0; i < records; i++, o += RecordSize)
        {
            int key = ReadIndex(data[o..]);
            delta._records[key] = new StructureRecord(
                BinaryPrimitives.ReadUInt64LittleEndian(data[(o + 2)..]),
                data[o + 10],
                data[o + 11],
                (StructureRotation)data[o + 12],
                (StructureType)data[o + 13]);
        }
    }

    private static void Require(ReadOnlySpan<byte> data, int length)
    {
        if (data.Length < length)
        {
            throw new FormatException("Truncated chunk delta.");
        }
    }

    private static int ReadIndex(ReadOnlySpan<byte> data)
    {
        int key = BinaryPrimitives.ReadUInt16LittleEndian(data);
        return key < Chunk2D.Size * Chunk2D.Size ? key : throw new FormatException("Chunk delta tile index out of range.");
    }

    /// <summary>Applies this delta onto a freshly generated chunk.</summary>
    /// <param name="chunk">The chunk, already populated from the seed.</param>
    public void ApplyTo(Chunk2D chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        foreach (KeyValuePair<int, MapTile> kvp in _tiles)
        {
            chunk.SetTile(kvp.Key & Chunk2D.SizeMask, kvp.Key >> Chunk2D.SizeBits, kvp.Value);
        }

        foreach (KeyValuePair<int, PropDestructibility> kvp in _propStates)
        {
            int index = chunk.FindPropIndexAt(kvp.Key & Chunk2D.SizeMask, kvp.Key >> Chunk2D.SizeBits);
            if (index >= 0)
            {
                chunk.SetProp(index, chunk.Props[index] with { Destructibility = kvp.Value });
            }
        }

        foreach (KeyValuePair<int, StructureTile> kvp in _structures)
        {
            if (!kvp.Value.IsOccupied)
            {
                continue;
            }

            int index = chunk.FindPropIndexAt(kvp.Key & Chunk2D.SizeMask, kvp.Key >> Chunk2D.SizeBits);
            if (index >= 0 && chunk.Props[index].Destructibility.IsDestructible && !chunk.Props[index].Destructibility.IsDestroyed)
            {
                chunk.SetProp(index, chunk.Props[index] with { Destructibility = chunk.Props[index].Destructibility.ApplyDamage(float.MaxValue) });
            }
        }
    }
}

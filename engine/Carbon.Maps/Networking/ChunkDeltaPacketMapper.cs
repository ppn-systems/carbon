// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Contracts.Maps;
using Carbon.Contracts.Maps.S2C;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;

namespace Carbon.Maps.Networking;

/// <summary>
/// Maps server-side <see cref="ChunkDelta"/> persistence/simulation diffs to and from <see cref="MapChunkDelta"/>
/// wire packets without reflection or runtime allocations beyond the wire arrays themselves.
/// </summary>
public static class ChunkDeltaPacketMapper
{
    /// <summary>
    /// Converts a domain <see cref="ChunkDelta"/> snapshot into a serializable <see cref="MapChunkDelta"/> packet.
    /// </summary>
    /// <param name="delta">Source chunk delta.</param>
    /// <param name="isEndOfStream">Whether this packet concludes the stream batch.</param>
    /// <returns>Populated <see cref="MapChunkDelta"/> wire DTO.</returns>
    public static MapChunkDelta ToPacket(ChunkDelta delta, bool isEndOfStream = true)
    {
        ArgumentNullException.ThrowIfNull(delta);

        int tileCount = delta.Tiles.Count;
        ushort[] tileIndices = tileCount == 0 ? [] : new ushort[tileCount];
        byte[] tileBiomes = tileCount == 0 ? [] : new byte[tileCount];
        byte[] tileHeightLevels = tileCount == 0 ? [] : new byte[tileCount];
        byte[] tileVariants = tileCount == 0 ? [] : new byte[tileCount];
        byte[] tileTraits = tileCount == 0 ? [] : new byte[tileCount];

        int ti = 0;
        foreach (KeyValuePair<int, MapTile> kvp in delta.Tiles)
        {
            tileIndices[ti] = (ushort)kvp.Key;
            tileBiomes[ti] = (byte)kvp.Value.Biome;
            tileHeightLevels[ti] = kvp.Value.HeightLevel;
            tileVariants[ti] = kvp.Value.Variant;
            tileTraits[ti] = (byte)(kvp.Value.IsSolid ? MapTileTraits.Solid : MapTileTraits.None);
            ti++;
        }

        int propCount = delta.PropStates.Count;
        ushort[] propIndices = propCount == 0 ? [] : new ushort[propCount];
        float[] propDurabilities = propCount == 0 ? [] : new float[propCount];
        bool[] propDestroyed = propCount == 0 ? [] : new bool[propCount];

        int pi = 0;
        foreach (KeyValuePair<int, PropDestructibility> kvp in delta.PropStates)
        {
            propIndices[pi] = (ushort)kvp.Key;
            propDurabilities[pi] = kvp.Value.CurrentDurability;
            propDestroyed[pi] = kvp.Value.IsDestroyed;
            pi++;
        }

        int structCount = delta.Structures.Count;
        ushort[] structIndices = structCount == 0 ? [] : new ushort[structCount];
        byte[] structTypes = structCount == 0 ? [] : new byte[structCount];
        float[] structDurabilities = structCount == 0 ? [] : new float[structCount];
        byte[] structDoorStates = structCount == 0 ? [] : new byte[structCount];
        byte[] structConnectivities = structCount == 0 ? [] : new byte[structCount];

        int si = 0;
        foreach (KeyValuePair<int, StructureTile> kvp in delta.Structures)
        {
            structIndices[si] = (ushort)kvp.Key;
            structTypes[si] = (byte)kvp.Value.Type;
            structDurabilities[si] = kvp.Value.CurrentDurability;
            structDoorStates[si] = (byte)kvp.Value.DoorState;
            structConnectivities[si] = (byte)kvp.Value.Connectivity;
            si++;
        }

        return new MapChunkDelta
        {
            IsEndOfStream = isEndOfStream,
            IsTerminator = false,
            ChunkX = delta.ChunkX,
            ChunkY = delta.ChunkY,
            Revision = delta.Revision,
            TileIndices = tileIndices,
            TileBiomes = tileBiomes,
            TileHeightLevels = tileHeightLevels,
            TileVariants = tileVariants,
            TileTraits = tileTraits,
            PropIndices = propIndices,
            PropDurabilities = propDurabilities,
            PropDestroyed = propDestroyed,
            StructureIndices = structIndices,
            StructureTypes = structTypes,
            StructureDurabilities = structDurabilities,
            StructureDoorStates = structDoorStates,
            StructureConnectivities = structConnectivities
        };
    }

    /// <summary>
    /// Reconstructs a <see cref="ChunkDelta"/> from a received <see cref="MapChunkDelta"/> wire packet.
    /// </summary>
    /// <param name="packet">Received chunk delta packet.</param>
    /// <returns>Hydrated domain <see cref="ChunkDelta"/> ready for <see cref="ChunkWorld2D.ImportDelta"/>.</returns>
    public static ChunkDelta FromPacket(MapChunkDelta packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        int tileCount = packet.TileIndices.Length;
        if (packet.TileBiomes.Length != tileCount ||
            packet.TileHeightLevels.Length != tileCount ||
            packet.TileVariants.Length != tileCount ||
            packet.TileTraits.Length != tileCount)
        {
            throw new ArgumentException("Tile array lengths in MapChunkDelta must match TileIndices.", nameof(packet));
        }

        int propCount = packet.PropIndices.Length;
        if (packet.PropDurabilities.Length != propCount || packet.PropDestroyed.Length != propCount)
        {
            throw new ArgumentException("Prop array lengths in MapChunkDelta must match PropIndices.", nameof(packet));
        }

        int structCount = packet.StructureIndices.Length;
        if (packet.StructureTypes.Length != structCount ||
            packet.StructureDurabilities.Length != structCount ||
            packet.StructureDoorStates.Length != structCount ||
            packet.StructureConnectivities.Length != structCount)
        {
            throw new ArgumentException("Structure array lengths in MapChunkDelta must match StructureIndices.", nameof(packet));
        }

        ChunkDelta delta = new(packet.ChunkX, packet.ChunkY);
        int maxLocalIndex = (Chunk2D.Size * Chunk2D.Size) - 1;

        for (int i = 0; i < tileCount; i++)
        {
            int idx = packet.TileIndices[i];
            if ((uint)idx > (uint)maxLocalIndex)
            {
                continue;
            }

            int lx = idx % Chunk2D.Size;
            int ly = idx / Chunk2D.Size;
            bool isSolid = ((MapTileTraits)packet.TileTraits[i] & MapTileTraits.Solid) != 0;
            MapTile tile = new(
                (BiomeType)packet.TileBiomes[i],
                Elevation: 0f,
                Temperature: 0f,
                Moisture: 0f,
                IsSolid: isSolid,
                Variant: packet.TileVariants[i],
                HeightLevel: packet.TileHeightLevels[i]);
            delta.SetTile(lx, ly, in tile);
        }

        for (int i = 0; i < propCount; i++)
        {
            int idx = packet.PropIndices[i];
            if ((uint)idx > (uint)maxLocalIndex)
            {
                continue;
            }

            int lx = idx % Chunk2D.Size;
            int ly = idx / Chunk2D.Size;
            float durability = MathF.Max(0f, packet.PropDurabilities[i]);
            bool destroyed = packet.PropDestroyed[i] || durability <= 0f;
            PropDestructibility state = new(
                IsDestructible: true,
                MaxDurability: MathF.Max(1f, durability),
                CurrentDurability: destroyed ? 0f : durability,
                HarvestTier: 1,
                IsDestroyed: destroyed);
            delta.SetPropState(lx, ly, state);
        }

        for (int i = 0; i < structCount; i++)
        {
            int idx = packet.StructureIndices[i];
            if ((uint)idx > (uint)maxLocalIndex)
            {
                continue;
            }

            int lx = idx % Chunk2D.Size;
            int ly = idx / Chunk2D.Size;
            StructureTile blueprint = BuildingGrid.CreateBlueprint((StructureType)packet.StructureTypes[i]);
            float durability = MathF.Max(0f, packet.StructureDurabilities[i]);
            bool destroyed = durability <= 0f;
            PropDestructibility destructibility = new(
                blueprint.Destructibility.IsDestructible,
                blueprint.Destructibility.MaxDurability,
                destroyed ? 0f : MathF.Min(blueprint.Destructibility.MaxDurability, durability),
                blueprint.Destructibility.HarvestTier,
                destroyed);

            StructureTile structure = blueprint with
            {
                Destructibility = destructibility,
                DoorState = (DoorState)packet.StructureDoorStates[i],
                Connectivity = (WallConnectivityMask)packet.StructureConnectivities[i]
            };
            delta.SetStructure(lx, ly, in structure);
        }

        delta.RestoreRevision(packet.Revision);
        return delta;
    }
}

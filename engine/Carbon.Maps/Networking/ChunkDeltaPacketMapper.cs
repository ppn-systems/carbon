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
        byte[] propStages = propCount == 0 ? [] : new byte[propCount];

        int pi = 0;
        foreach (KeyValuePair<int, PropDestructibility> kvp in delta.PropStates)
        {
            propIndices[pi] = (ushort)kvp.Key;
            propDurabilities[pi] = kvp.Value.CurrentDurability;
            propDestroyed[pi] = kvp.Value.IsDestroyed;
            propStages[pi] = (byte)delta.GrowthStageAt(kvp.Key);
            pi++;
        }

        int plantedCount = delta.PlantedProps.Count;
        ushort[] plantedIndices = plantedCount == 0 ? [] : new ushort[plantedCount];
        byte[] plantedTypes = plantedCount == 0 ? [] : new byte[plantedCount];
        int pl = 0;
        foreach (KeyValuePair<int, MapPropType> kvp in delta.PlantedProps)
        {
            plantedIndices[pl] = (ushort)kvp.Key;
            plantedTypes[pl] = (byte)kvp.Value;
            pl++;
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

        int siteCount = delta.Sites.Count;
        ushort[] siteIndices = siteCount == 0 ? [] : new ushort[siteCount];
        byte[] siteTypes = siteCount == 0 ? [] : new byte[siteCount];
        byte[] siteRotations = siteCount == 0 ? [] : new byte[siteCount];
        byte[] siteStages = siteCount == 0 ? [] : new byte[siteCount];
        byte[] siteSizes = siteCount == 0 ? [] : new byte[siteCount];
        float[] siteWorkDone = siteCount == 0 ? [] : new float[siteCount];
        ulong[] siteOwners = siteCount == 0 ? [] : new ulong[siteCount];
        int ci = 0;
        foreach (KeyValuePair<int, ConstructionSite> kvp in delta.Sites)
        {
            ConstructionSite site = kvp.Value;
            siteIndices[ci] = (ushort)kvp.Key;
            siteTypes[ci] = (byte)site.Type;
            siteRotations[ci] = (byte)site.Rotation;
            siteStages[ci] = (byte)site.Stage;
            siteSizes[ci] = (byte)(site.Width | (site.Height << 4));
            siteWorkDone[ci] = site.WorkDone;
            siteOwners[ci] = site.OwnerId;
            ci++;
        }

        int recordCount = delta.StructureRecords.Count;
        ushort[] recordIndices = recordCount == 0 ? [] : new ushort[recordCount];
        ulong[] recordOwners = recordCount == 0 ? [] : new ulong[recordCount];
        byte[] recordOffsets = recordCount == 0 ? [] : new byte[recordCount];
        byte[] recordRotations = recordCount == 0 ? [] : new byte[recordCount];
        byte[] recordFloors = recordCount == 0 ? [] : new byte[recordCount];
        int ri = 0;
        foreach (KeyValuePair<int, StructureRecord> kvp in delta.StructureRecords)
        {
            StructureRecord record = kvp.Value;
            recordIndices[ri] = (ushort)kvp.Key;
            recordOwners[ri] = record.OwnerId;
            recordOffsets[ri] = (byte)((record.AnchorOffsetX & 0x0F) | ((record.AnchorOffsetY & 0x0F) << 4));
            recordRotations[ri] = (byte)record.Rotation;
            recordFloors[ri] = (byte)record.FloorBeneath;
            ri++;
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
            PropGrowthStages = propStages,
            PlantedIndices = plantedIndices,
            PlantedTypes = plantedTypes,
            StructureIndices = structIndices,
            StructureTypes = structTypes,
            StructureDurabilities = structDurabilities,
            StructureDoorStates = structDoorStates,
            StructureConnectivities = structConnectivities,
            SiteIndices = siteIndices,
            SiteTypes = siteTypes,
            SiteRotations = siteRotations,
            SiteStages = siteStages,
            SiteSizes = siteSizes,
            SiteWorkDone = siteWorkDone,
            SiteOwnerIds = siteOwners,
            RecordIndices = recordIndices,
            RecordOwnerIds = recordOwners,
            RecordAnchorOffsets = recordOffsets,
            RecordRotations = recordRotations,
            RecordFloorsBeneath = recordFloors
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
        if (packet.PropDurabilities.Length != propCount || packet.PropDestroyed.Length != propCount || packet.PropGrowthStages.Length != propCount)
        {
            throw new ArgumentException("Prop array lengths in MapChunkDelta must match PropIndices.", nameof(packet));
        }

        if (packet.PlantedTypes.Length != packet.PlantedIndices.Length)
        {
            throw new ArgumentException("Planted array lengths in MapChunkDelta must match PlantedIndices.", nameof(packet));
        }

        int structCount = packet.StructureIndices.Length;
        if (packet.StructureTypes.Length != structCount ||
            packet.StructureDurabilities.Length != structCount ||
            packet.StructureDoorStates.Length != structCount ||
            packet.StructureConnectivities.Length != structCount)
        {
            throw new ArgumentException("Structure array lengths in MapChunkDelta must match StructureIndices.", nameof(packet));
        }

        int siteCount = packet.SiteIndices.Length;
        if (packet.SiteTypes.Length != siteCount ||
            packet.SiteRotations.Length != siteCount ||
            packet.SiteStages.Length != siteCount ||
            packet.SiteSizes.Length != siteCount ||
            packet.SiteWorkDone.Length != siteCount ||
            packet.SiteOwnerIds.Length != siteCount)
        {
            throw new ArgumentException("Site array lengths in MapChunkDelta must match SiteIndices.", nameof(packet));
        }

        int recordCount = packet.RecordIndices.Length;
        if (packet.RecordOwnerIds.Length != recordCount ||
            packet.RecordAnchorOffsets.Length != recordCount ||
            packet.RecordRotations.Length != recordCount ||
            packet.RecordFloorsBeneath.Length != recordCount)
        {
            throw new ArgumentException("Record array lengths in MapChunkDelta must match RecordIndices.", nameof(packet));
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

        for (int i = 0; i < packet.PlantedIndices.Length; i++)
        {
            int idx = packet.PlantedIndices[i];
            MapPropType type = (MapPropType)packet.PlantedTypes[i];
            if ((uint)idx <= (uint)maxLocalIndex && MapProp.IsTree(type))
            {
                delta.SetPlanted(idx % Chunk2D.Size, idx / Chunk2D.Size, type);
            }
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
            TreeGrowthStage stage = (TreeGrowthStage)packet.PropGrowthStages[i];
            if (stage is not (TreeGrowthStage.Mature or TreeGrowthStage.Stump or TreeGrowthStage.Sapling or TreeGrowthStage.Young))
            {
                stage = TreeGrowthStage.Mature;
            }

            // The client keeps no growth timers or tree types: the server replicates every stage change.
            delta.SetPropState(lx, ly, state, 0UL, new TreeGrowth(stage, MapPropType.None));
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

        for (int i = 0; i < siteCount; i++)
        {
            int idx = packet.SiteIndices[i];
            int width = packet.SiteSizes[i] & 0x0F;
            int height = packet.SiteSizes[i] >> 4;
            ConstructionStage stage = (ConstructionStage)packet.SiteStages[i];
            if ((uint)idx > (uint)maxLocalIndex || width == 0 || height == 0 ||
                stage is not (ConstructionStage.Blueprint or ConstructionStage.Frame))
            {
                continue;
            }

            // Delivered quantities stay server-side; clients only render stage and progress.
            ConstructionSite site = new((StructureType)packet.SiteTypes[i], (StructureRotation)packet.SiteRotations[i], packet.SiteOwnerIds[i], width, height, costEntries: 0)
            {
                Stage = stage,
                WorkDone = MathF.Max(0f, packet.SiteWorkDone[i])
            };
            delta.SetSite(idx % Chunk2D.Size, idx / Chunk2D.Size, site);
        }

        for (int i = 0; i < recordCount; i++)
        {
            int idx = packet.RecordIndices[i];
            if ((uint)idx > (uint)maxLocalIndex)
            {
                continue;
            }

            byte offsets = packet.RecordAnchorOffsets[i];
            StructureRecord record = new(
                packet.RecordOwnerIds[i],
                (byte)(offsets & 0x0F),
                (byte)(offsets >> 4),
                (StructureRotation)packet.RecordRotations[i],
                (StructureType)packet.RecordFloorsBeneath[i]);
            delta.SetStructureRecord(idx % Chunk2D.Size, idx / Chunk2D.Size, record);
        }

        delta.RestoreRevision(packet.Revision);
        return delta;
    }
}

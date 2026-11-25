// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Props;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class LandmarkAndHydrologyRulesTests
{
    [Theory]
    [InlineData(42UL)]
    [InlineData(7UL)]
    public void PointsOfInterest_FollowTheirPlacementRules(ulong seed)
    {
        ProceduralWorldGenerator generator = new(seed);
        int villages = 0;
        for (int sy = -4; sy <= 4; sy++)
        {
            for (int sx = -4; sx <= 4; sx++)
            {
                PointOfInterest poi = generator.Landmarks.GetSite(sx, sy);
                if (poi.Type == PointOfInterestType.None)
                {
                    continue;
                }

                MapTile tile = generator.SampleTile(poi.TileX, poi.TileY);
                float river = generator.GetRiverDistance(poi.TileX, poi.TileY);
                Assert.False(tile.IsSolid);

                switch (poi.Type)
                {
                    case PointOfInterestType.RiverVillage:
                        villages++;
                        Assert.Equal(0, tile.HeightLevel);
                        Assert.InRange(river, 4.5f, 11f); // close to water, outside the flood zone
                        break;
                    case PointOfInterestType.MountainShrine:
                        Assert.Equal(2, tile.HeightLevel);
                        break;
                    case PointOfInterestType.LakesideCamp:
                        Assert.InRange(river, 2.2f, 4.5f);
                        break;
                    case PointOfInterestType.Harbor:
                        Assert.Equal(0, tile.HeightLevel);
                        Assert.True(river <= 12f);
                        break;
                    case PointOfInterestType.DesertVillage:
                    case PointOfInterestType.SnowVillage:
                    case PointOfInterestType.SwampVillage:
                        villages++;
                        Assert.Equal(0, tile.HeightLevel);
                        Assert.InRange(river, 3f, 11f);
                        break;
                    case PointOfInterestType.WorldTree:
                        Assert.True(tile.HeightLevel <= 1);
                        Assert.True(river > 8f, "A World Tree grows well away from rivers.");
                        break;
                    case PointOfInterestType.None:
                    default:
                        break;
                }
            }
        }

        Assert.True(villages > 0, "A 2000x2000 tile area should hold at least one river village.");
    }

    [Fact]
    public void WorldTrees_AreRareButPresent()
    {
        ProceduralWorldGenerator generator = new(7UL);

        // Density over a fixed 11 x 11 block of sites (~2,500 x 2,500 tiles).
        int trees = 0;
        for (int sy = -5; sy <= 5; sy++)
        {
            for (int sx = -5; sx <= 5; sx++)
            {
                trees += generator.Landmarks.GetSite(sx, sy).Type == PointOfInterestType.WorldTree ? 1 : 0;
            }
        }

        Assert.True(trees > 0, "No World Tree in 121 sites.");
        Assert.True(trees <= 121 / 10, $"World Trees should be rare: {trees} in 121 sites.");
    }

    [Fact]
    public void GenerateChunk_PointOfInterestFootprint_HasNoNaturalProps()
    {
        ProceduralWorldGenerator generator = new(42UL);
        for (int sy = -4; sy <= 4; sy++)
        {
            for (int sx = -4; sx <= 4; sx++)
            {
                PointOfInterest poi = generator.Landmarks.GetSite(sx, sy);
                if (poi.Type == PointOfInterestType.None)
                {
                    continue;
                }

                Chunk2D chunk = generator.GenerateChunk(Chunk2D.ToChunkCoord(poi.TileX), Chunk2D.ToChunkCoord(poi.TileY));
                Assert.Contains(poi, chunk.PointsOfInterest);
                for (int i = 0; i < chunk.Props.Count; i++)
                {
                    MapProp p = chunk.Props[i];
                    int wx = (chunk.ChunkX * Chunk2D.Size) + p.LocalTileX;
                    int wy = (chunk.ChunkY * Chunk2D.Size) + p.LocalTileY;
                    Assert.False(poi.Contains(wx, wy), $"{p.Type} inside {poi.Type} footprint at ({wx}, {wy}).");
                }

                return;
            }
        }
    }
}

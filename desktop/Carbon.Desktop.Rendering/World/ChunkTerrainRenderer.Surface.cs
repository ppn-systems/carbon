// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Procedural;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;

namespace Carbon.Desktop.Rendering.World;

/// <content>
/// Ground baking: gradient tiles whose corner colours blend the biomes that meet there, crisp land/water
/// edges with 45 degree corner cuts, and the biome blueprints' ground decals.
/// </content>
public sealed partial class ChunkTerrainRenderer
{
    // The chunk's tiles plus a two-tile border (shore shading reads the 4x4 tiles around each corner).
    private const int BakeBorder = 2;
    private const int BakeStride = Chunk2D.Size + (2 * BakeBorder);
    private const int CornerStride = Chunk2D.Size + 1;

    private static readonly MapTile[] s_bakeTiles = new MapTile[BakeStride * BakeStride];
    private static readonly TerrainSurface.Field[] s_cornerFields = new TerrainSurface.Field[CornerStride * CornerStride];
    private static readonly BiomeSurfaceStyle?[] s_fallbackStyles = new BiomeSurfaceStyle?[byte.MaxValue + 1];

    // Corner colours per (surface kind, height) perspective: up to four tiles ask for each corner, nearly
    // always from one or two perspectives, so two slots per corner cache them for the bake.
    private static readonly CornerSlot[] s_cornerSlotsA = new CornerSlot[CornerStride * CornerStride];
    private static readonly CornerSlot[] s_cornerSlotsB = new CornerSlot[CornerStride * CornerStride];

    // Shore water is lifted towards this sandy turquoise over about two tiles; land at the waterline darkens
    // (wet bank) over about one.
    private static readonly Vector3 s_shoreShelf = new(150f, 206f, 204f);
    private const float ShoreShelfBlend = 0.5f;
    private const float WetBankDarkening = 0.24f;

    /// <summary>
    /// Bakes the complete static geometry of one chunk (ground, decals, cliffs, ramps, structures, bridges and
    /// roads) into <paramref name="builder"/>. Pure CPU work: no GPU calls, so it can be measured headless.
    /// </summary>
    /// <param name="builder">Destination builder (not cleared).</param>
    /// <param name="chunk">The chunk.</param>
    /// <param name="world">The streaming world (neighbour tiles, nav grid, buildings, roads).</param>
    /// <param name="art">Vector art repository (biome blueprints), or <c>null</c> for flat fallbacks.</param>
    internal static void BakeChunkGeometry(ChunkMeshBuilder builder, Chunk2D chunk, ChunkWorld2D world, IVectorArtRepository? art)
    {
        s_bakingWaterfalls.Clear();
        s_bakingRipples.Clear();
        float tileSize = world.TileSize;
        chunk.GetBounds(tileSize, out float cMinX, out float cMinY, out _, out _);
        FillBakeCaches(chunk, world);

        for (int ly = 0; ly < Chunk2D.Size; ly++)
        {
            float tileWorldY = cMinY + (ly * tileSize);
            for (int lx = 0; lx < Chunk2D.Size; lx++)
            {
                float tileWorldX = cMinX + (lx * tileSize);
                ref readonly MapTile tile = ref chunk.GetTile(lx, ly);
                int wtx = (chunk.ChunkX * Chunk2D.Size) + lx;
                int wty = (chunk.ChunkY * Chunk2D.Size) + ly;

                // Ground: a colour per corner (blended with the neighbours) and the tile's own at the centre.
                BiomeSurfaceStyle style = StyleOf(tile.Biome, art);
                Vector3 nw = CornerGround(lx, ly, in tile, art);
                Vector3 ne = CornerGround(lx + 1, ly, in tile, art);
                Vector3 se = CornerGround(lx + 1, ly + 1, in tile, art);
                Vector3 sw = CornerGround(lx, ly + 1, in tile, art);

                // The centre is the tile's own ground pulled halfway towards its corners, so a gradient tile
                // shows no diamond where its corners differ from it.
                TerrainSurface.Field centre = TerrainSurface.Sample(wtx + 0.5f, wty + 0.5f);
                Vector3 own = TerrainSurface.Ground(style, tile.HeightLevel, in centre);
                builder.AddGradientRectangle(
                    new RectangleF(tileWorldX, tileWorldY, tileSize, tileSize),
                    TerrainSurface.ToColor(nw),
                    TerrainSurface.ToColor(ne),
                    TerrainSurface.ToColor(se),
                    TerrainSurface.ToColor(sw),
                    TerrainSurface.ToColor(Vector3.Lerp(own, (nw + ne + se + sw) * 0.25f, 0.5f)));

                // About one water tile in ten gets an animated ripple
                if (TerrainSurface.IsWater(tile.Biome))
                {
                    int rippleSeed = (wtx * 92837111) ^ (wty * 689287499);
                    if (((uint)rippleSeed % 100u) < 11u)
                    {
                        s_bakingRipples.Add(new RippleSpot(
                            new Vector2(tileWorldX + (tileSize * 0.5f), tileWorldY + (tileSize * 0.5f)),
                            rippleSeed,
                            tile.Biome == BiomeType.DeepOcean));
                    }
                }

                // 45 degree cuts where land meets water in a staircase
                BakeTileDiagonalCorners(builder, lx, ly, tileWorldX, tileWorldY, in tile, tileSize, art);

                // Ground details (blueprint decals), under cliff faces and structures
                if (art is not null && art.TryGetBiome((byte)tile.Biome, out CompiledBiomeArt? biomeArt))
                {
                    bool nearWater = IsNearWater(lx, ly, in tile);
                    BakeTileMicroDetails(builder, biomeArt, wtx, wty, tileWorldX, tileWorldY, tileSize);
                    TerrainSurface.BakeDecals(builder, biomeArt, wtx, wty, tileWorldX, tileWorldY, tileSize, nearWater);
                }

                // 2.5D Cliff Drop Faces & Drop Shadows
                BakeTileCliffFaces(builder, chunk, wtx, wty, tileWorldX, tileWorldY, in tile, world, tileSize, art);

                // Foam and spray at the foot of waterfalls
                BakeTileWaterfallSplash(builder, wtx, wty, tileWorldX, tileWorldY, in tile, world, tileSize);

                // Ramps (Incline passages connecting height levels)
                BakeTileRamps(builder, wtx, wty, tileWorldX, tileWorldY, in tile, world, tileSize, art);

                // Building Structures (Walls, Doors, Floors, Roof Interior Shading)
                BakeTileBuildings(builder, wtx, wty, tileWorldX, tileWorldY, world, tileSize, art);
            }
        }

        BakeBridges(builder, chunk, tileSize);
        BakeRoads(builder, chunk, world, tileSize);
    }

    private static ref readonly MapTile BakeTile(int lx, int ly) => ref s_bakeTiles[((ly + BakeBorder) * BakeStride) + lx + BakeBorder];

    private static void FillBakeCaches(Chunk2D chunk, ChunkWorld2D world)
    {
        int x0 = chunk.ChunkX * Chunk2D.Size;
        int y0 = chunk.ChunkY * Chunk2D.Size;
        for (int ly = -BakeBorder; ly < Chunk2D.Size + BakeBorder; ly++)
        {
            for (int lx = -BakeBorder; lx < Chunk2D.Size + BakeBorder; lx++)
            {
                s_bakeTiles[((ly + BakeBorder) * BakeStride) + lx + BakeBorder] = (uint)lx < Chunk2D.Size && (uint)ly < Chunk2D.Size
                    ? chunk.GetTile(lx, ly)
                    : world.PeekTileAtWorld(x0 + lx, y0 + ly);
            }
        }

        for (int cy = 0; cy < CornerStride; cy++)
        {
            for (int cx = 0; cx < CornerStride; cx++)
            {
                s_cornerFields[(cy * CornerStride) + cx] = TerrainSurface.Sample(x0 + cx, y0 + cy);
            }
        }

        Array.Clear(s_cornerSlotsA);
        Array.Clear(s_cornerSlotsB);
    }

    private static BiomeSurfaceStyle StyleOf(BiomeType biome, IVectorArtRepository? art)
    {
        if (art is not null && art.TryGetBiome((byte)biome, out CompiledBiomeArt? biomeArt))
        {
            return biomeArt.Surface;
        }

        // No blueprint: a flat style from the engine's biome colour, created once per biome.
        ref BiomeSurfaceStyle? slot = ref s_fallbackStyles[(byte)biome];
        if (slot is null)
        {
            uint hex = BiomeProperties.Get(biome).ColorRgba;
            slot = BiomeSurfaceStyle.FromGround(new Vector3((hex >> 24) & 0xFF, (hex >> 16) & 0xFF, (hex >> 8) & 0xFF));
        }

        return slot;
    }

    // Colour of the corner (cx, cy) (local corner coordinates, 0..32) as seen from a tile of `self`'s kind:
    // the average ground of the tiles sharing the corner that are the same kind of surface (land or water) at
    // the same height. Every tile of that kind computes the same value, so ground blends smoothly across biome
    // borders while cliffs and shorelines stay crisp. Near the other kind, water lifts towards a sandy shelf
    // and land darkens (wet bank), by how much of the surrounding 4x4 tiles is the other kind.
    private static ColorRgba CornerColor(int cx, int cy, in MapTile self, IVectorArtRepository? art) =>
        TerrainSurface.ToColor(CornerGround(cx, cy, in self, art));

    private static Vector3 CornerGround(int cx, int cy, in MapTile self, IVectorArtRepository? art)
    {
        bool selfWater = TerrainSurface.IsWater(self.Biome);
        int key = ((self.HeightLevel << 1) | (selfWater ? 1 : 0)) + 1;
        int index = (cy * CornerStride) + cx;
        ref CornerSlot a = ref s_cornerSlotsA[index];
        if (a.Key == key)
        {
            return a.Value;
        }

        ref CornerSlot b = ref s_cornerSlotsB[index];
        if (b.Key == key)
        {
            return b.Value;
        }

        Vector3 value = ComputeCornerGround(cx, cy, in self, selfWater, art);
        ref CornerSlot slot = ref a.Key == 0 ? ref a : ref b;
        slot = new CornerSlot(key, value);
        return value;
    }

    private static Vector3 ComputeCornerGround(int cx, int cy, in MapTile self, bool selfWater, IVectorArtRepository? art)
    {
        ref readonly TerrainSurface.Field field = ref s_cornerFields[(cy * CornerStride) + cx];
        Vector3 sum = Vector3.Zero;
        int count = 0;
        for (int i = 0; i < 4; i++)
        {
            ref readonly MapTile t = ref BakeTile(cx - 1 + (i & 1), cy - 1 + (i >> 1));
            if (t.HeightLevel == self.HeightLevel && TerrainSurface.IsWater(t.Biome) == selfWater)
            {
                sum += TerrainSurface.Ground(StyleOf(t.Biome, art), t.HeightLevel, in field);
                count++;
            }
        }

        Vector3 c = count > 0 ? sum / count : TerrainSurface.Ground(StyleOf(self.Biome, art), self.HeightLevel, in field);
        int other = 0;
        for (int dy = -2; dy < 2; dy++)
        {
            for (int dx = -2; dx < 2; dx++)
            {
                other += TerrainSurface.IsWater(BakeTile(cx + dx, cy + dy).Biome) != selfWater ? 1 : 0;
            }
        }

        if (other == 0)
        {
            return c;
        }

        // A straight shore puts 8 of the 16 tiles on the other side at the waterline, 4 one tile away.
        float shore = Math.Clamp(other / 7f, 0f, 1f);
        return selfWater
            ? Vector3.Lerp(c, s_shoreShelf, shore * shore * ShoreShelfBlend)
            : c * (1f - (WetBankDarkening * shore * shore * shore));
    }

    private readonly record struct CornerSlot(int Key, Vector3 Value);

    private static bool IsNearWater(int lx, int ly, in MapTile self)
    {
        bool selfWater = TerrainSurface.IsWater(self.Biome);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (TerrainSurface.IsWater(BakeTile(lx + dx, ly + dy).Biome) != selfWater)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Marching-squares style corner cut: when both edge neighbours of a corner (and the diagonal one) are the
    // other kind of surface (water against land) at the same height, the water takes the half-tile triangle of
    // the corner, shaded with the water's own corner colours. A staircase shoreline becomes a straight 45
    // degree line. Land/land and water/water borders need no cut: they blend through the corner colours.
    private static void BakeTileDiagonalCorners(
        ChunkMeshBuilder builder, int lx, int ly, float x, float y, in MapTile tile, float s, IVectorArtRepository? art)
    {
        if (TerrainSurface.IsWater(tile.Biome))
        {
            return;
        }

        ref readonly MapTile n = ref BakeTile(lx, ly - 1);
        ref readonly MapTile so = ref BakeTile(lx, ly + 1);
        ref readonly MapTile w = ref BakeTile(lx - 1, ly);
        ref readonly MapTile e = ref BakeTile(lx + 1, ly);

        TryCut(builder, in tile, in n, in w, in BakeTile(lx - 1, ly - 1), (lx, ly), (lx + 1, ly), (lx, ly + 1), new Vector2(x, y), new Vector2(x + s, y), new Vector2(x, y + s), art);
        TryCut(builder, in tile, in n, in e, in BakeTile(lx + 1, ly - 1), (lx + 1, ly), (lx + 1, ly + 1), (lx, ly), new Vector2(x + s, y), new Vector2(x + s, y + s), new Vector2(x, y), art);
        TryCut(builder, in tile, in so, in w, in BakeTile(lx - 1, ly + 1), (lx, ly + 1), (lx, ly), (lx + 1, ly + 1), new Vector2(x, y + s), new Vector2(x, y), new Vector2(x + s, y + s), art);
        TryCut(builder, in tile, in so, in e, in BakeTile(lx + 1, ly + 1), (lx + 1, ly + 1), (lx, ly + 1), (lx + 1, ly), new Vector2(x + s, y + s), new Vector2(x, y + s), new Vector2(x + s, y), art);

        static void TryCut(
            ChunkMeshBuilder b,
            in MapTile self,
            in MapTile a,
            in MapTile c,
            in MapTile diag,
            (int X, int Y) k0,
            (int X, int Y) k1,
            (int X, int Y) k2,
            Vector2 corner,
            Vector2 p1,
            Vector2 p2,
            IVectorArtRepository? repo)
        {
            if (!TerrainSurface.IsWater(a.Biome) || !TerrainSurface.IsWater(c.Biome) || !TerrainSurface.IsWater(diag.Biome) ||
                a.HeightLevel != self.HeightLevel || c.HeightLevel != self.HeightLevel || diag.HeightLevel != self.HeightLevel)
            {
                return;
            }

            b.AddTriangle(
                corner,
                p1,
                p2,
                CornerColor(k0.X, k0.Y, in a, repo),
                CornerColor(k1.X, k1.Y, in a, repo),
                CornerColor(k2.X, k2.Y, in a, repo));
        }
    }

    // The blueprint's fixed micro details (one stamp at the drawn position on some tiles).
    private static void BakeTileMicroDetails(
        ChunkMeshBuilder builder, CompiledBiomeArt biomeArt, int wtx, int wty, float x, float y, float tileSize)
    {
        if (biomeArt.DetailVariants.Length == 0)
        {
            return;
        }

        uint hash = (uint)((wtx * 374761393) ^ (wty * 668265263));
        if ((hash % 100u) < (uint)biomeArt.DetailChancePercent)
        {
            int variantIdx = (int)((hash >> 8) % (uint)biomeArt.DetailVariants.Length);
            bool mirrorX = ((hash >> 16) & 1u) != 0;
            VectorMeshBaker.StampTileShapes(builder, biomeArt.DetailVariants[variantIdx], x, y, tileSize, mirrorX);
        }
    }
}

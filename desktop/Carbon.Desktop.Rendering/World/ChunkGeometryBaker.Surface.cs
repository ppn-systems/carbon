// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Procedural;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Streaming;
using static Carbon.Desktop.Rendering.World.ChunkTerrainRenderer;

namespace Carbon.Desktop.Rendering.World;

/// <content>
/// Ground baking: gradient tiles whose corner colours blend the biomes that meet there, crisp land/water
/// edges with 45 degree corner cuts, and the biome blueprints' ground decals.
/// </content>
internal sealed partial class ChunkGeometryBaker
{
    // The chunk's tiles plus a two-tile border (shore shading reads the 4x4 tiles around each corner).
    private const int BakeBorder = 2;
    private const int BakeStride = Chunk2D.Size + (2 * BakeBorder);
    private const int CornerStride = Chunk2D.Size + 1;

    private readonly MapTile[] _bakeTiles = new MapTile[BakeStride * BakeStride];
    private readonly TerrainSurface.Field[] _cornerFields = new TerrainSurface.Field[CornerStride * CornerStride];
    private readonly BiomeSurfaceStyle?[] _fallbackStyles = new BiomeSurfaceStyle?[byte.MaxValue + 1];

    // Corner colours per (surface kind, height) perspective: up to four tiles ask for each corner, nearly
    // always from one or two perspectives, so two slots per corner cache them for the bake.
    private readonly CornerSlot[] _cornerSlotsA = new CornerSlot[CornerStride * CornerStride];
    private readonly CornerSlot[] _cornerSlotsB = new CornerSlot[CornerStride * CornerStride];

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
    public void Bake(ChunkMeshBuilder builder, Chunk2D chunk, ChunkWorld2D world, IVectorArtRepository? art)
    {
        _waterfalls.Clear();
        _ripples.Clear();
        float tileSize = world.TileSize;
        chunk.GetBounds(tileSize, out float cMinX, out float cMinY, out _, out _);
        this.FillBakeCaches(chunk, world);

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
                BiomeSurfaceStyle style = this.StyleOf(tile.Biome, art);
                Vector3 nw = this.CornerGround(lx, ly, in tile, art);
                Vector3 ne = this.CornerGround(lx + 1, ly, in tile, art);
                Vector3 se = this.CornerGround(lx + 1, ly + 1, in tile, art);
                Vector3 sw = this.CornerGround(lx, ly + 1, in tile, art);

                // The centre is the tile's own ground pulled halfway towards its corners, so a gradient tile
                // shows no diamond where its corners differ from it.
                TerrainSurface.Field centre = TerrainSurface.Sample(wtx + 0.5f, wty + 0.5f);
                Vector3 own = TerrainSurface.Ground(style, tile.HeightLevel, in centre);
                if (TerrainSurface.IsWater(tile.Biome))
                {
                    own *= TerrainSurface.WaterDepthShade(WaterDepth.Of(tile));
                }

                builder.AddGradientRectangle(
                    new RectangleF(tileWorldX, tileWorldY, tileSize, tileSize),
                    TerrainSurface.ToColor(nw),
                    TerrainSurface.ToColor(ne),
                    TerrainSurface.ToColor(se),
                    TerrainSurface.ToColor(sw),
                    TerrainSurface.ToColor(Vector3.Lerp(own, (nw + ne + se + sw) * 0.25f, 0.5f)));

                // Every water tile on the shoreline gets lapping foam; about one open-water tile in ten gets a ripple.
                if (TerrainSurface.IsWater(tile.Biome))
                {
                    int rippleSeed = (wtx * 92837111) ^ (wty * 689287499);
                    Vector2 toShore = this.ToShore(lx, ly);
                    if (toShore != Vector2.Zero)
                    {
                        _ripples.Add(new RippleSpot(
                            new Vector2(tileWorldX + (tileSize * 0.5f), tileWorldY + (tileSize * 0.5f)),
                            rippleSeed,
                            tile.Biome == BiomeType.DeepOcean,
                            toShore));
                    }
                    else if (((uint)rippleSeed % 100u) < 11u)
                    {
                        _ripples.Add(new RippleSpot(
                            new Vector2(tileWorldX + (tileSize * 0.5f), tileWorldY + (tileSize * 0.5f)),
                            rippleSeed,
                            tile.Biome == BiomeType.DeepOcean));
                    }
                }

                // 45 degree cuts where land meets water in a staircase
                this.BakeTileDiagonalCorners(builder, lx, ly, tileWorldX, tileWorldY, in tile, tileSize, art);

                // Ground details (blueprint decals), under cliff faces and structures
                if (art is not null && art.TryGetBiome((byte)tile.Biome, out CompiledBiomeArt? biomeArt))
                {
                    bool nearWater = this.IsNearWater(lx, ly, in tile);
                    BakeTileMicroDetails(builder, biomeArt, wtx, wty, tileWorldX, tileWorldY, tileSize);
                    TerrainSurface.BakeDecals(builder, biomeArt, wtx, wty, tileWorldX, tileWorldY, tileSize, nearWater);
                }

                // 2.5D Cliff Drop Faces & Drop Shadows
                this.BakeTileCliffFaces(builder, chunk, wtx, wty, tileWorldX, tileWorldY, in tile, world, tileSize, art);

                // Foam and spray at the foot of waterfalls
                this.BakeTileWaterfallSplash(builder, wtx, wty, tileWorldX, tileWorldY, in tile, world, tileSize);

                // Ramps (Incline passages connecting height levels)
                this.BakeTileRamps(builder, wtx, wty, tileWorldX, tileWorldY, in tile, world, tileSize, art);

                // Building Structures (Walls, Doors, Floors, Roof Interior Shading)
                BakeTileBuildings(builder, wtx, wty, tileWorldX, tileWorldY, world, tileSize, art);
            }
        }

        this.BakeBridges(builder, chunk, tileSize);
        this.BakeRoads(builder, chunk, world, tileSize);
    }

    private ref readonly MapTile BakeTile(int lx, int ly) => ref _bakeTiles[((ly + BakeBorder) * BakeStride) + lx + BakeBorder];

    private void FillBakeCaches(Chunk2D chunk, ChunkWorld2D world)
    {
        int x0 = chunk.ChunkX * Chunk2D.Size;
        int y0 = chunk.ChunkY * Chunk2D.Size;
        for (int ly = -BakeBorder; ly < Chunk2D.Size + BakeBorder; ly++)
        {
            for (int lx = -BakeBorder; lx < Chunk2D.Size + BakeBorder; lx++)
            {
                _bakeTiles[((ly + BakeBorder) * BakeStride) + lx + BakeBorder] = (uint)lx < Chunk2D.Size && (uint)ly < Chunk2D.Size
                    ? chunk.GetTile(lx, ly)
                    : world.PeekTileAtWorld(x0 + lx, y0 + ly);
            }
        }

        for (int cy = 0; cy < CornerStride; cy++)
        {
            for (int cx = 0; cx < CornerStride; cx++)
            {
                _cornerFields[(cy * CornerStride) + cx] = TerrainSurface.Sample(x0 + cx, y0 + cy);
            }
        }

        Array.Clear(_cornerSlotsA);
        Array.Clear(_cornerSlotsB);
    }

    private BiomeSurfaceStyle StyleOf(BiomeType biome, IVectorArtRepository? art)
    {
        if (art is not null && art.TryGetBiome((byte)biome, out CompiledBiomeArt? biomeArt))
        {
            return biomeArt.Surface;
        }

        // No blueprint: a flat style from the engine's biome colour, created once per biome.
        ref BiomeSurfaceStyle? slot = ref _fallbackStyles[(byte)biome];
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
    private ColorRgba CornerColor(int cx, int cy, in MapTile self, IVectorArtRepository? art) =>
        TerrainSurface.ToColor(this.CornerGround(cx, cy, in self, art));

    private Vector3 CornerGround(int cx, int cy, in MapTile self, IVectorArtRepository? art)
    {
        bool selfWater = TerrainSurface.IsWater(self.Biome);
        int key = ((self.HeightLevel << 1) | (selfWater ? 1 : 0)) + 1;
        int index = (cy * CornerStride) + cx;
        ref CornerSlot a = ref _cornerSlotsA[index];
        if (a.Key == key)
        {
            return a.Value;
        }

        ref CornerSlot b = ref _cornerSlotsB[index];
        if (b.Key == key)
        {
            return b.Value;
        }

        Vector3 value = this.ComputeCornerGround(cx, cy, in self, selfWater, art);
        ref CornerSlot slot = ref a.Key == 0 ? ref a : ref b;
        slot = new CornerSlot(key, value);
        return value;
    }

    private Vector3 ComputeCornerGround(int cx, int cy, in MapTile self, bool selfWater, IVectorArtRepository? art)
    {
        ref readonly TerrainSurface.Field field = ref _cornerFields[(cy * CornerStride) + cx];
        Vector3 sum = Vector3.Zero;
        float depthSum = 0f;
        int count = 0;
        for (int i = 0; i < 4; i++)
        {
            ref readonly MapTile t = ref this.BakeTile(cx - 1 + (i & 1), cy - 1 + (i >> 1));
            if (t.HeightLevel == self.HeightLevel && TerrainSurface.IsWater(t.Biome) == selfWater)
            {
                sum += TerrainSurface.Ground(this.StyleOf(t.Biome, art), t.HeightLevel, in field);
                depthSum += WaterDepth.Of(t);
                count++;
            }
        }

        Vector3 c = count > 0 ? sum / count : TerrainSurface.Ground(this.StyleOf(self.Biome, art), self.HeightLevel, in field);
        if (selfWater)
        {
            // Water darkens smoothly with depth (ADR-032), blended across corners like the ground colour.
            c *= TerrainSurface.WaterDepthShade(count > 0 ? depthSum / count : WaterDepth.Of(self));
        }
        int other = 0;
        for (int dy = -2; dy < 2; dy++)
        {
            for (int dx = -2; dx < 2; dx++)
            {
                other += TerrainSurface.IsWater(this.BakeTile(cx + dx, cy + dy).Biome) != selfWater ? 1 : 0;
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

    // The direction from a water tile toward the land beside it (sum of the land side neighbours, normalised), or
    // zero when no side touches land.
    private Vector2 ToShore(int lx, int ly)
    {
        Vector2 sum = Vector2.Zero;
        for (int i = 0; i < 4; i++)
        {
            int dx = i switch { 0 => 1, 1 => -1, _ => 0 };
            int dy = i switch { 2 => 1, 3 => -1, _ => 0 };
            MapTile n = this.BakeTile(lx + dx, ly + dy);
            if (!TerrainSurface.IsWater(n.Biome))
            {
                sum += new Vector2(dx, dy);
            }
        }

        return sum == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(sum);
    }

    private bool IsNearWater(int lx, int ly, in MapTile self)
    {
        bool selfWater = TerrainSurface.IsWater(self.Biome);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (TerrainSurface.IsWater(this.BakeTile(lx + dx, ly + dy).Biome) != selfWater)
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
    private void BakeTileDiagonalCorners(
        ChunkMeshBuilder builder, int lx, int ly, float x, float y, in MapTile tile, float s, IVectorArtRepository? art)
    {
        if (TerrainSurface.IsWater(tile.Biome))
        {
            return;
        }

        ref readonly MapTile n = ref this.BakeTile(lx, ly - 1);
        ref readonly MapTile so = ref this.BakeTile(lx, ly + 1);
        ref readonly MapTile w = ref this.BakeTile(lx - 1, ly);
        ref readonly MapTile e = ref this.BakeTile(lx + 1, ly);

        TryCut(builder, in tile, in n, in w, in this.BakeTile(lx - 1, ly - 1), (lx, ly), (lx + 1, ly), (lx, ly + 1), new Vector2(x, y), new Vector2(x + s, y), new Vector2(x, y + s), art);
        TryCut(builder, in tile, in n, in e, in this.BakeTile(lx + 1, ly - 1), (lx + 1, ly), (lx + 1, ly + 1), (lx, ly), new Vector2(x + s, y), new Vector2(x + s, y + s), new Vector2(x, y), art);
        TryCut(builder, in tile, in so, in w, in this.BakeTile(lx - 1, ly + 1), (lx, ly + 1), (lx, ly), (lx + 1, ly + 1), new Vector2(x, y + s), new Vector2(x, y), new Vector2(x + s, y + s), art);
        TryCut(builder, in tile, in so, in e, in this.BakeTile(lx + 1, ly + 1), (lx + 1, ly + 1), (lx, ly + 1), (lx + 1, ly), new Vector2(x + s, y + s), new Vector2(x, y + s), new Vector2(x + s, y), art);

        void TryCut(
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
                this.CornerColor(k0.X, k0.Y, in a, repo),
                this.CornerColor(k1.X, k1.Y, in a, repo),
                this.CornerColor(k2.X, k2.Y, in a, repo));
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

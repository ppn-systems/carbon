// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Procedural;
using Carbon.Graphics.Batching.Shaders;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.OpenGL.States;
using Carbon.Graphics.OpenGL.Textures;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Roads;
using Carbon.Maps.Streaming;
using Carbon.Maps.Topography;
using GlShader = Carbon.Graphics.OpenGL.Shaders.Shader;

namespace Carbon.Desktop.Rendering.World;

/// <summary>
/// High-performance static GPU mesh renderer and cache for procedural terrain chunks.
/// Employs Minecraft-style static VBO/EBO chunk baking: compiles 1024 tiles + corner autotiling
/// into static GPU buffers once on chunk load, rendering entire visible chunks in 1 draw call per chunk.
/// Reduces CPU draw overhead by 95% and eliminates per-frame vertex copies.
/// </summary>
public sealed partial class ChunkTerrainRenderer : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly IVectorArtRepository? _art;
    private readonly GlStateCache _stateCache;
    private readonly GlShader _shader;
    private readonly int _uViewProjLocation;
    private readonly int _uTextureLocation;
    private readonly Texture2D _whiteTexture;
    private readonly ChunkMeshBuilder _builder = new();
    private readonly Dictionary<long, ChunkMesh2D> _meshCache = new();
    private readonly List<long> _evictionBuffer = new();
    private readonly Dictionary<long, WaterfallEdge[]> _waterfalls = new();
    private readonly Dictionary<long, RippleSpot[]> _ripples = new();

    // Water tiles chosen for animated ripples while baking the current chunk.
    private static readonly List<RippleSpot> s_bakingRipples = new();

    /// <summary>A point on open water where an animated ripple glint plays.</summary>
    public readonly record struct RippleSpot(Vector2 Centre, int Seed, bool IsDeep);

    /// <summary>Gets the ripple spots of a chunk (empty until the chunk has been rendered once).</summary>
    public ReadOnlySpan<RippleSpot> GetRipples(long chunkKey) =>
        _ripples.TryGetValue(chunkKey, out RippleSpot[]? spots) ? spots : [];

    // Waterfall edges found while baking the current chunk (the bake helpers are static).
    private static readonly List<WaterfallEdge> s_bakingWaterfalls = new();

    /// <summary>The lip of a waterfall: the curtain hangs CliffFaceHeight pixels below the edge A-B.</summary>
    public readonly record struct WaterfallEdge(Vector2 A, Vector2 B, int Seed);

    /// <summary>
    /// Gets the waterfall edges baked into a chunk's mesh, for the animated overlay. Empty until the chunk
    /// has been rendered once.
    /// </summary>
    public ReadOnlySpan<WaterfallEdge> GetWaterfalls(long chunkKey) =>
        _waterfalls.TryGetValue(chunkKey, out WaterfallEdge[]? edges) ? edges : [];
    private bool _hasBegun;
    private bool _disposed;

    // Bake statistics, reported every 64 bakes and on dispose. The first bake (JIT warm-up) is left out.
    private long _bakeTicks;
    private long _bakeVertices;
    private int _bakeCount;

    /// <summary>
    /// Gets the average time spent baking one chunk mesh after the first, in milliseconds (0 until two chunks
    /// have been baked).
    /// </summary>
    public double AverageBakeMilliseconds => _bakeCount < 2 ? 0d : Stopwatch.GetElapsedTime(0, _bakeTicks).TotalMilliseconds / (_bakeCount - 1);

    private void ReportBakeStats()
    {
        if (_bakeCount >= 2)
        {
            Console.WriteLine(
                $"[Terrain] {_bakeCount} chunks baked: {this.AverageBakeMilliseconds:F2} ms and {_bakeVertices / _bakeCount} vertices per chunk on average");
        }
    }

    /// <summary>
    /// Gets the count of currently cached static chunk GPU meshes.
    /// </summary>
    public int CachedMeshCount => _meshCache.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkTerrainRenderer"/> class.
    /// </summary>
    /// <param name="device">The graphics device used for GPU buffer allocation.</param>
    /// <param name="art">Optional vector art repository supplying biome &amp; structure <c>.cvg.json</c> blueprints.</param>
    public ChunkTerrainRenderer(IGraphicsDevice device, IVectorArtRepository? art = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _art = art;

        if (device is GraphicsDevice concrete)
        {
            _stateCache = concrete.StateCache;
        }
        else
        {
            throw new ArgumentException("The graphics device must be a valid GraphicsDevice instance.", nameof(device));
        }

        _shader = BuiltinShaders.CreateSpriteShader(device.Gl, _stateCache);
        _uViewProjLocation = _shader.GetUniformLocation("uViewProjection");
        _uTextureLocation = _shader.GetUniformLocation("uTexture");

        // 1x1 white texture for zero-asset colored vertex rendering
        _whiteTexture = new Texture2D(device.Gl, _stateCache, 1, 1, TextureFormat.Rgba8);
        ReadOnlySpan<byte> whitePixel = [255, 255, 255, 255];
        _whiteTexture.SetData(whitePixel);
    }

    /// <summary>
    /// Begins the static chunk mesh rendering pass.
    /// Binds the terrain shader, sets camera view-projection matrix, and binds the texture atlas once.
    /// </summary>
    /// <param name="viewProjection">Camera view-projection transformation matrix.</param>
    public void Begin(in Matrix4x4 viewProjection)
    {
        if (_hasBegun)
        {
            throw new InvalidOperationException("ChunkTerrainRenderer.Begin cannot be called again before End.");
        }

        _shader.Bind();
        if (_uViewProjLocation != -1)
        {
            _shader.SetUniform(_uViewProjLocation, in viewProjection);
        }

        if (_uTextureLocation != -1)
        {
            _shader.SetUniform(_uTextureLocation, 0);
        }

        _stateCache.SetBlendState(BlendState.NonPremultiplied);
        _stateCache.SetDepthStencilState(DepthStencilState.None);
        _hasBegun = true;
    }

    /// <summary>
    /// Renders a chunk's terrain in a single high-efficiency GPU draw call.
    /// Lazily bakes the static VBO/EBO mesh if not already cached.
    /// </summary>
    /// <param name="chunk">The active chunk to render.</param>
    /// <param name="world">Active chunk world streaming coordinator.</param>
    public void RenderChunk(Chunk2D chunk, ChunkWorld2D world)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(world);

        if (!_hasBegun)
        {
            throw new InvalidOperationException("ChunkTerrainRenderer.Begin must be called before RenderChunk.");
        }

        long key = chunk.Key;
        if (!_meshCache.TryGetValue(key, out ChunkMesh2D? mesh))
        {
            s_bakingWaterfalls.Clear();
            s_bakingRipples.Clear();
            long start = Stopwatch.GetTimestamp();
            mesh = this.BuildChunkMesh(chunk, world);
            if (++_bakeCount > 1)
            {
                _bakeTicks += Stopwatch.GetTimestamp() - start;
            }

            if (_bakeCount % 64 == 0)
            {
                this.ReportBakeStats();
            }

            _meshCache[key] = mesh;
            _waterfalls[key] = [.. s_bakingWaterfalls];
            _ripples[key] = [.. s_bakingRipples];
        }

        mesh.Draw();
    }

    /// <summary>
    /// Ends the static chunk mesh rendering pass.
    /// </summary>
    public void End()
    {
        if (!_hasBegun)
        {
            throw new InvalidOperationException("ChunkTerrainRenderer.End called without Begin.");
        }

        _hasBegun = false;
    }

    /// <summary>
    /// Evicts and disposes static GPU meshes for chunks located beyond the specified eviction radius.
    /// </summary>
    /// <param name="centerChunkX">Current player center chunk X coordinate.</param>
    /// <param name="centerChunkY">Current player center chunk Y coordinate.</param>
    /// <param name="evictionRadius">Distance in chunks beyond which mesh buffers are freed.</param>
    public void EvictDistant(int centerChunkX, int centerChunkY, int evictionRadius)
    {
        _evictionBuffer.Clear();

        foreach (KeyValuePair<long, ChunkMesh2D> kvp in _meshCache)
        {
            (int cx, int cy) = Chunk2D.UnpackKey(kvp.Key);
            int dist = Math.Max(Math.Abs(cx - centerChunkX), Math.Abs(cy - centerChunkY));
            if (dist > evictionRadius)
            {
                _evictionBuffer.Add(kvp.Key);
            }
        }

        for (int i = 0; i < _evictionBuffer.Count; i++)
        {
            long key = _evictionBuffer[i];
            _ = _waterfalls.Remove(key);
            _ = _ripples.Remove(key);
            if (_meshCache.Remove(key, out ChunkMesh2D? mesh))
            {
                mesh.Dispose();
            }
        }
    }

    /// <summary>
    /// Invalidates and disposes the cached GPU mesh for the specified chunk and its 8 immediate neighbors
    /// so autotiling and building changes rebuild cleanly on the next render pass.
    /// </summary>
    /// <param name="chunkX">Modified chunk X coordinate.</param>
    /// <param name="chunkY">Modified chunk Y coordinate.</param>
    public void InvalidateChunk(int chunkX, int chunkY)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                long key = Chunk2D.PackKey(chunkX + dx, chunkY + dy);
                _ = _waterfalls.Remove(key);
                _ = _ripples.Remove(key);
                if (_meshCache.Remove(key, out ChunkMesh2D? mesh))
                {
                    mesh.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Invalidates and disposes all cached chunk GPU meshes (e.g., after reseeding).
    /// </summary>
    public void InvalidateAll()
    {
        foreach (ChunkMesh2D mesh in _meshCache.Values)
        {
            mesh.Dispose();
        }

        _meshCache.Clear();
        _waterfalls.Clear();
        _ripples.Clear();
    }

    private ChunkMesh2D BuildChunkMesh(Chunk2D chunk, ChunkWorld2D world)
    {
        _builder.Clear();
        BakeChunkGeometry(_builder, chunk, world, _art);
        chunk.GetBounds(world.TileSize, out float cMinX, out float cMinY, out float cMaxX, out float cMaxY);
        RectangleF bounds = new(cMinX, cMinY, cMaxX - cMinX, cMaxY - cMinY);
        _bakeVertices += _builder.VertexCount;
        return _builder.Build(_device, _whiteTexture, bounds);
    }

    private static void BakeBridges(ChunkMeshBuilder builder, Chunk2D chunk, float tileSize)
    {
        ColorRgba plankShadow = new(0, 0, 0, 65);
        ColorRgba railColor = new(96, 62, 34, 255);
        ColorRgba plankLight = new(156, 110, 66, 255);
        ColorRgba plankDark = new(132, 90, 50, 255);

        for (int ly = 0; ly < Chunk2D.Size; ly++)
        {
            for (int lx = 0; lx < Chunk2D.Size; lx++)
            {
                if (!chunk.IsBridge(lx, ly))
                {
                    continue;
                }

                float wx = ((chunk.ChunkX * Chunk2D.Size) + lx) * tileSize;
                float wy = ((chunk.ChunkY * Chunk2D.Size) + ly) * tileSize;
                bool northBridge = ly > 0 && chunk.IsBridge(lx, ly - 1);
                bool southBridge = ly + 1 < Chunk2D.Size && chunk.IsBridge(lx, ly + 1);
                bool horizontal = !northBridge && !southBridge;

                builder.AddRectangle(new RectangleF(wx + 2f, wy + 4f, tileSize, tileSize), plankShadow);
                if (horizontal)
                {
                    builder.AddRectangle(new RectangleF(wx, wy + 2f, tileSize, tileSize - 4f), plankDark);
                    for (int p = 0; p < 3; p++)
                    {
                        float px = wx + (p * (tileSize / 3f)) + 1f;
                        builder.AddRectangle(new RectangleF(px, wy + 4f, (tileSize / 3f) - 2f, tileSize - 8f), (p & 1) == 0 ? plankLight : plankDark);
                    }

                    builder.AddRectangle(new RectangleF(wx, wy + 1f, tileSize, 3f), railColor);
                    builder.AddRectangle(new RectangleF(wx, wy + tileSize - 4f, tileSize, 3f), railColor);
                }
                else
                {
                    builder.AddRectangle(new RectangleF(wx + 2f, wy, tileSize - 4f, tileSize), plankDark);
                    for (int p = 0; p < 3; p++)
                    {
                        float py = wy + (p * (tileSize / 3f)) + 1f;
                        builder.AddRectangle(new RectangleF(wx + 4f, py, tileSize - 8f, (tileSize / 3f) - 2f), (p & 1) == 0 ? plankLight : plankDark);
                    }

                    builder.AddRectangle(new RectangleF(wx + 1f, wy, 3f, tileSize), railColor);
                    builder.AddRectangle(new RectangleF(wx + tileSize - 4f, wy, 3f, tileSize), railColor);
                }
            }
        }
    }

    private static readonly List<RoadLink> s_roadLinks = new();

    // Dirt roads: overlapping discs stamped every half tile along each road curve, on the road tiles of this
    // chunk only (water, cliffs and other levels stay as they are). A darker rim, a lighter worn centre and a
    // few pebbles keep the path soft-edged and organic.
    private static void BakeRoads(ChunkMeshBuilder builder, Chunk2D chunk, ChunkWorld2D world, float tileSize)
    {
        int x0 = chunk.ChunkX * Chunk2D.Size;
        int y0 = chunk.ChunkY * Chunk2D.Size;
        s_roadLinks.Clear();
        world.Generator.Roads.Query(x0, y0, x0 + Chunk2D.Size - 1, y0 + Chunk2D.Size - 1, s_roadLinks);
        if (s_roadLinks.Count == 0)
        {
            return;
        }

        for (int pass = 0; pass < 3; pass++)
        {
            foreach (RoadLink link in s_roadLinks)
            {
                float[] p = link.Points;
                for (int i = 0; i + 3 < p.Length; i += 2)
                {
                    float ax = p[i];
                    float ay = p[i + 1];
                    float bx = p[i + 2];
                    float by = p[i + 3];
                    int steps = Math.Max(1, (int)(MathF.Sqrt(((bx - ax) * (bx - ax)) + ((by - ay) * (by - ay))) * 2f));
                    for (int k = 0; k < steps; k++)
                    {
                        float t = k / (float)steps;
                        float x = ax + ((bx - ax) * t);
                        float y = ay + ((by - ay) * t);
                        int lx = (int)MathF.Floor(x) - x0;
                        int ly = (int)MathF.Floor(y) - y0;
                        if (!chunk.IsRoad(lx, ly))
                        {
                            continue;
                        }

                        Vector2 c = new(x * tileSize, y * tileSize);
                        int h = ((int)(x * 7.3f) * 73856093) ^ ((int)(y * 7.3f) * 19349663);
                        switch (pass)
                        {
                            case 0:
                                AddDisc(builder, c, tileSize * 0.86f, new ColorRgba(120, 92, 58, 255));
                                break;
                            case 1:
                                AddDisc(builder, c, tileSize * 0.66f, new ColorRgba(168, 134, 90, 255));
                                break;
                            default:
                                AddDisc(builder, c + new Vector2(0f, tileSize * 0.05f), tileSize * 0.34f, new ColorRgba(188, 156, 110, 255));
                                if (((uint)h % 9u) == 0u)
                                {
                                    float ox = ((((uint)h >> 8) % 100u / 100f) - 0.5f) * tileSize;
                                    float oy = ((((uint)h >> 16) % 100u / 100f) - 0.5f) * tileSize * 0.8f;
                                    AddDisc(builder, c + new Vector2(ox, oy), tileSize * 0.07f, new ColorRgba(132, 120, 104, 255));
                                }

                                break;
                        }
                    }
                }
            }
        }
    }

    private static void AddDisc(ChunkMeshBuilder builder, Vector2 c, float r, ColorRgba color)
    {
        const int Segments = 12;
        Vector2 prev = c + new Vector2(r, 0f);
        for (int i = 1; i <= Segments; i++)
        {
            float a = i * (MathF.Tau / Segments);
            Vector2 next = c + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r * 0.85f);
            builder.AddTriangle(c, prev, next, color);
            prev = next;
        }
    }

    private static (ColorRgba rockBase, ColorRgba rockStrata, ColorRgba rockHighlight, ColorRgba rockShadow) GetCliffPalette(
        BiomeType biome,
        IVectorArtRepository? art)
    {
        if (art is not null && art.TryGetBiome((byte)biome, out CompiledBiomeArt? biomeArt))
        {
            return (biomeArt.CliffBase, biomeArt.CliffStrata, biomeArt.CliffHighlight, biomeArt.CliffShadow);
        }

        return biome switch
        {
            BiomeType.RedMesa => (
                new ColorRgba(160, 80, 56, 255),
                new ColorRgba(135, 65, 44, 255),
                new ColorRgba(215, 125, 88, 230),
                new ColorRgba(95, 42, 28, 255)
            ),
            BiomeType.FrozenTundra => (
                new ColorRgba(125, 140, 155, 255),
                new ColorRgba(105, 120, 135, 255),
                new ColorRgba(220, 232, 245, 240),
                new ColorRgba(75, 90, 105, 255)
            ),
            BiomeType.AlpinePeaks => (
                new ColorRgba(165, 180, 198, 255),
                new ColorRgba(135, 150, 168, 255),
                new ColorRgba(240, 248, 255, 250),
                new ColorRgba(95, 110, 128, 255)
            ),
            BiomeType.DuneDesert => (
                new ColorRgba(180, 128, 70, 255),
                new ColorRgba(155, 105, 55, 255),
                new ColorRgba(225, 168, 95, 230),
                new ColorRgba(115, 75, 35, 255)
            ),
            BiomeType.Savanna => (
                new ColorRgba(175, 135, 80, 255),
                new ColorRgba(145, 110, 60, 255),
                new ColorRgba(220, 180, 120, 230),
                new ColorRgba(110, 80, 40, 255)
            ),
            BiomeType.VolcanicAshland => (
                new ColorRgba(45, 44, 50, 255),
                new ColorRgba(34, 33, 38, 255),
                new ColorRgba(105, 105, 115, 230),
                new ColorRgba(22, 21, 25, 255)
            ),
            BiomeType.BlossomWoods => (
                new ColorRgba(85, 90, 88, 255),
                new ColorRgba(65, 72, 70, 255),
                new ColorRgba(210, 180, 195, 230),
                new ColorRgba(45, 50, 48, 255)
            ),
            BiomeType.AncientForest or BiomeType.LushPlains or BiomeType.MistySwamp or BiomeType.TropicalJungle or
            BiomeType.DeepOcean or BiomeType.ShallowWater or BiomeType.BeachSand or _ => (
                new ColorRgba(70, 76, 82, 255),
                new ColorRgba(54, 59, 65, 255),
                new ColorRgba(165, 172, 180, 230),
                new ColorRgba(36, 40, 45, 255)
            )
        };
    }

    private static ColorRgba ResolveBiomeColor(BiomeType biome, uint fallbackColorHex, IVectorArtRepository? art) =>
        art is not null && art.TryGetBiome((byte)biome, out CompiledBiomeArt? biomeArt)
            ? biomeArt.GroundColor
            : HexToColor(fallbackColorHex);

    private const float CliffFaceHeight = 14f;

    private static readonly ColorRgba s_foam = new(255, 255, 255, 235);
    private static readonly ColorRgba s_foamSoft = new(224, 242, 254, 170);
    private static readonly ColorRgba s_curtainTop = new(110, 205, 245, 235);
    private static readonly ColorRgba s_curtainBottom = new(40, 150, 210, 235);
    private static readonly ColorRgba s_streak = new(200, 236, 252, 220);

    // Quad as two triangles: a, b along the top edge, then the bottom edge offset by `drop`.
    private static void AddSlantedBand(ChunkMeshBuilder builder, Vector2 a, Vector2 b, float top, float height, ColorRgba color)
    {
        Vector2 a0 = a + new Vector2(0f, top);
        Vector2 b0 = b + new Vector2(0f, top);
        Vector2 a1 = a0 + new Vector2(0f, height);
        Vector2 b1 = b0 + new Vector2(0f, height);
        builder.AddTriangle(a0, b0, b1, color);
        builder.AddTriangle(a0, b1, a1, color);
    }

    // A cliff face (rock) or a waterfall curtain hanging under the edge a-b (a-b may be slanted).
    private static void AddFace(
        ChunkMeshBuilder builder, Vector2 a, Vector2 b, bool isWater, int wtx, int wty,
        ColorRgba rockBase, ColorRgba rockStrata, ColorRgba rockHighlight, ColorRgba rockShadow)
    {
        const float H = CliffFaceHeight;
        if (!isWater)
        {
            AddSlantedBand(builder, a, b, 0f, H, rockBase);
            AddSlantedBand(builder, a, b, 0f, 2f, rockHighlight);
            AddSlantedBand(builder, a, b, 5f, 2.5f, rockStrata);
            AddSlantedBand(builder, a, b, 10f, 1.5f, rockStrata);
            AddSlantedBand(builder, a, b, H - 2f, 2f, rockShadow);
            return;
        }

        // Waterfall: lighter top half, deeper bottom half, a foaming lip, and falling streaks.
        s_bakingWaterfalls.Add(new WaterfallEdge(a, b, (wtx * 73856093) ^ (wty * 19349663)));
        AddSlantedBand(builder, a, b, 0f, H * 0.5f, s_curtainTop);
        AddSlantedBand(builder, a, b, H * 0.5f, H * 0.5f, s_curtainBottom);
        Vector2 dir = b - a;
        uint h = (uint)((wtx * 73856093) ^ (wty * 19349663));
        for (int i = 0; i < 5; i++)
        {
            h = (h ^ (h >> 13)) * 0x5BD1E995;
            float t = ((i + 0.5f) / 5f) + (((((h >> 8) & 0xFF) / 255f) - 0.5f) * 0.12f);
            float w = 1.5f + ((h & 3) * 0.75f);
            Vector2 p = a + (dir * t);
            float len = H * (0.55f + (((h >> 16) & 0xFF) / 255f * 0.45f));
            builder.AddRectangle(new RectangleF(p.X - (w * 0.5f), p.Y + 2f, w, len), (i & 1) == 0 ? s_streak : s_foamSoft);
        }

        // Foam lip with small bubbles along the edge
        AddSlantedBand(builder, a, b, -1f, 3f, s_foam);
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = a + (dir * ((i + 0.5f) / 4f));
            builder.AddRectangle(new RectangleF(p.X - 2.5f, p.Y - 2.5f, 5f, 3f), s_foam);
        }
    }

    // Splash pool drawn on the lower tile right below a waterfall (the falling tile's own cell cannot draw
    // there: the next row of tiles is drawn after it and would cover the splash).
    private static void BakeTileWaterfallSplash(
        ChunkMeshBuilder builder, int wtx, int wty, float x, float y, in MapTile tile, ChunkWorld2D world, float tileSize)
    {
        if (tile.Biome != BiomeType.ShallowWater)
        {
            return;
        }

        MapTile north = world.PeekTileAtWorld(wtx, wty - 1);
        if (north.Biome != BiomeType.ShallowWater || north.HeightLevel <= tile.HeightLevel)
        {
            return;
        }

        // Scattered foam clumps rather than a full-width bar, so slanted falls do not leave a staircase.
        float s = tileSize;
        uint h = (uint)((wtx * 83492791) ^ (wty * 19349663));
        for (int i = 0; i < 4; i++)
        {
            h = (h ^ (h >> 13)) * 0x5BD1E995;
            float bx = x + 2f + ((h & 0xFF) / 255f * (s - 14f));
            float by = y + 1f + (((h >> 8) & 0xFF) / 255f * 8f);
            float w = 7f + ((h >> 16) & 7);
            builder.AddRectangle(new RectangleF(bx, by, w, 3f), s_foam);
            builder.AddRectangle(new RectangleF(bx + 1f, by + 3f, w - 2f, 2f), s_foamSoft);
        }
    }

    private static void BakeTileCliffFaces(
        ChunkMeshBuilder builder,
        Chunk2D chunk,
        int wtx,
        int wty,
        float x,
        float y,
        in MapTile tile,
        ChunkWorld2D world,
        float tileSize,
        IVectorArtRepository? art)
    {
        if (tile.HeightLevel == 0)
        {
            return;
        }

        CliffFaceMask mask = CliffAutotiler.ComputeCliffMask(wtx, wty, (tx, ty) =>
        {
            int tcx = Chunk2D.ToChunkCoord(tx);
            int tcy = Chunk2D.ToChunkCoord(ty);
            if (tcx == chunk.ChunkX && tcy == chunk.ChunkY)
            {
                int tlx = Chunk2D.ToLocalCoord(tx);
                int tly = Chunk2D.ToLocalCoord(ty);
                return chunk.GetTile(tlx, tly).HeightLevel;
            }

            return world.PeekTileAtWorld(tx, ty).HeightLevel;
        });

        if (mask == CliffFaceMask.None)
        {
            return;
        }

        float s = tileSize;
        (ColorRgba rockBase, ColorRgba rockStrata, ColorRgba rockHighlight, ColorRgba rockShadow) = GetCliffPalette(tile.Biome, art);
        ColorRgba dropShadow = new(0, 0, 0, 80);

        const float H = CliffFaceHeight;
        bool isWater = tile.Biome == BiomeType.ShallowWater;
        bool south = (mask & CliffFaceMask.South) != 0 &&
            world.NavGrid.GetRamp(wtx, wty) != RampDirection.South &&
            world.NavGrid.GetRamp(wtx, wty + 1) != RampDirection.South;
        bool east = (mask & CliffFaceMask.East) != 0 &&
            world.NavGrid.GetRamp(wtx, wty) != RampDirection.East &&
            world.NavGrid.GetRamp(wtx + 1, wty) != RampDirection.East;
        bool west = (mask & CliffFaceMask.West) != 0 &&
            world.NavGrid.GetRamp(wtx, wty) != RampDirection.West &&
            world.NavGrid.GetRamp(wtx - 1, wty) != RampDirection.West;

        // Outer corners on a diagonal edge are chamfered: one slanted face replaces the south face and the
        // side face, and the lower ground fills the cut-away triangle. Consecutive corner tiles line up,
        // so a diagonal cliff (or waterfall) reads as one straight slope instead of a staircase.
        if (south && (east ^ west))
        {
            MapTile below = world.PeekTileAtWorld(wtx, wty + 1);
            int lx = wtx - (chunk.ChunkX * Chunk2D.Size);
            int ly = wty - (chunk.ChunkY * Chunk2D.Size);
            ColorRgba nw = CornerColor(lx, ly, in below, art);
            ColorRgba ne = CornerColor(lx + 1, ly, in below, art);
            ColorRgba se = CornerColor(lx + 1, ly + 1, in below, art);
            ColorRgba sw = CornerColor(lx, ly + 1, in below, art);
            if (east)
            {
                Vector2 a = new(x, y + s - H);
                Vector2 b = new(x + s, y - H);
                builder.AddTriangle(new Vector2(x + s, y), new Vector2(x + s, y + s), new Vector2(x, y + s), ne, se, sw);
                AddFace(builder, a, b, isWater, wtx, wty, rockBase, rockStrata, rockHighlight, rockShadow);
                if (!isWater)
                {
                    builder.AddTriangle(new Vector2(x, y + s), new Vector2(x + s, y), new Vector2(x + s, y + 6f), dropShadow);
                }
            }
            else
            {
                Vector2 a = new(x, y - H);
                Vector2 b = new(x + s, y + s - H);
                builder.AddTriangle(new Vector2(x, y), new Vector2(x + s, y + s), new Vector2(x, y + s), nw, se, sw);
                AddFace(builder, a, b, isWater, wtx, wty, rockBase, rockStrata, rockHighlight, rockShadow);
                if (!isWater)
                {
                    builder.AddTriangle(new Vector2(x, y), new Vector2(x + s, y + s), new Vector2(x, y + 6f), dropShadow);
                }
            }

            east = false;
            west = false;
            south = false;
        }

        // 1. South Face: the 2.5D wall (or waterfall curtain) facing the camera
        if (south)
        {
            AddFace(builder, new Vector2(x, y + s - H), new Vector2(x + s, y + s - H), isWater, wtx, wty, rockBase, rockStrata, rockHighlight, rockShadow);
        }

        // 2. North Face: Top edge drop shadow down onto northern tile
        if ((mask & CliffFaceMask.North) != 0)
        {
            if (world.NavGrid.GetRamp(wtx, wty) != RampDirection.North &&
                world.NavGrid.GetRamp(wtx, wty - 1) != RampDirection.North)
            {
                builder.AddRectangle(new RectangleF(x, y, s, 2.5f), rockShadow);
                builder.AddRectangle(new RectangleF(x, y - 6f, s, 6f), dropShadow);
            }
        }

        // 3. East / West faces: seen from above as narrow strips
        if (east)
        {
            if (isWater)
            {
                builder.AddRectangle(new RectangleF(x + s - 7f, y, 7f, s), s_curtainTop);
                builder.AddRectangle(new RectangleF(x + s - 3f, y, 3f, s), s_curtainBottom);
                builder.AddRectangle(new RectangleF(x + s - 8f, y, 2f, s), s_foam);
            }
            else
            {
                builder.AddRectangle(new RectangleF(x + s - 7f, y, 7f, s), rockBase);
                builder.AddRectangle(new RectangleF(x + s - 7f, y, 1.5f, s), rockHighlight);
                builder.AddRectangle(new RectangleF(x + s - 3f, y, 3f, s), rockShadow);
            }
        }

        if (west)
        {
            if (isWater)
            {
                builder.AddRectangle(new RectangleF(x, y, 7f, s), s_curtainTop);
                builder.AddRectangle(new RectangleF(x, y, 3f, s), s_curtainBottom);
                builder.AddRectangle(new RectangleF(x + 6f, y, 2f, s), s_foam);
            }
            else
            {
                builder.AddRectangle(new RectangleF(x, y, 7f, s), rockBase);
                builder.AddRectangle(new RectangleF(x + 5.5f, y, 1.5f, s), rockHighlight);
                builder.AddRectangle(new RectangleF(x, y, 2f, s), rockShadow);
            }
        }
    }

    private static void BakeTileRamps(
        ChunkMeshBuilder builder,
        int wtx,
        int wty,
        float x,
        float y,
        in MapTile tile,
        ChunkWorld2D world,
        float tileSize,
        IVectorArtRepository? art)
    {
        RampDirection ramp = world.NavGrid.GetRamp(wtx, wty);
        if (ramp == RampDirection.None)
        {
            return;
        }

        float s = tileSize;
        ColorRgba slopeBed = ResolveBiomeColor(tile.Biome, tile.ColorRgba, art);
        (ColorRgba rockCheek, _, _, _) = GetCliffPalette(tile.Biome, art);

        // Subtle natural foot-worn trail down the center
        ColorRgba wornDirt = new(
            (byte)Math.Clamp((slopeBed.R * 0.88f) + 10, 0, 255),
            (byte)Math.Clamp((slopeBed.G * 0.88f) + 8, 0, 255),
            (byte)Math.Clamp((slopeBed.B * 0.82f) + 6, 0, 255),
            slopeBed.A);

        ColorRgba embeddedStone = new(95, 102, 108, 120);

        if (ramp is RampDirection.West or RampDirection.East)
        {
            // Horizontal Natural Mountain Pass (West <-> East)
            const float apronReach = 6f;
            float trailX = ramp == RampDirection.West ? x - apronReach : x;
            float trailW = s + apronReach;

            // 1. Natural earthen chute bed connecting the height levels
            builder.AddRectangle(new RectangleF(trailX, y + 2f, trailW, s - 4f), slopeBed);

            // 2. Foot-worn center dirt trail
            builder.AddRectangle(new RectangleF(trailX, y + 6f, trailW, s - 12f), wornDirt);

            // 3. Flanking natural rock shoulders framing the pass
            builder.AddRectangle(new RectangleF(trailX, y, trailW, 2.5f), rockCheek);
            builder.AddRectangle(new RectangleF(trailX, y + s - 2.5f, trailW, 2.5f), rockCheek);

            // 4. Subtle embedded natural stepping stones
            builder.AddRectangle(new RectangleF(trailX + (trailW * 0.30f), y + 9f, 3f, s - 18f), embeddedStone);
            builder.AddRectangle(new RectangleF(trailX + (trailW * 0.70f), y + 8f, 3f, s - 16f), embeddedStone);

            // 5. Soft ground shadow at lower terminus
            float footX = ramp == RampDirection.West ? trailX - 2f : trailX + trailW;
            builder.AddRectangle(new RectangleF(footX, y + 2f, 2.5f, s - 4f), new ColorRgba(0, 0, 0, 45));
        }
        else
        {
            // Vertical Natural Mountain Pass (North <-> South)
            const float apronReach = 6f;
            float trailY = ramp == RampDirection.South ? y : y - apronReach;
            float trailH = s + apronReach;

            // 1. Natural earthen chute bed connecting the height levels
            builder.AddRectangle(new RectangleF(x + 2f, trailY, s - 4f, trailH), slopeBed);

            // 2. Foot-worn center dirt trail
            builder.AddRectangle(new RectangleF(x + 6f, trailY, s - 12f, trailH), wornDirt);

            // 3. Flanking natural rock shoulders framing the pass
            builder.AddRectangle(new RectangleF(x, trailY, 2.5f, trailH), rockCheek);
            builder.AddRectangle(new RectangleF(x + s - 2.5f, trailY, 2.5f, trailH), rockCheek);

            // 4. Subtle embedded natural stepping stones
            builder.AddRectangle(new RectangleF(x + 9f, trailY + (trailH * 0.30f), s - 18f, 3f), embeddedStone);
            builder.AddRectangle(new RectangleF(x + 8f, trailY + (trailH * 0.70f), s - 16f, 3f), embeddedStone);

            // 5. Soft ground shadow at lower terminus
            float footY = ramp == RampDirection.South ? trailY + trailH : trailY - 2f;
            builder.AddRectangle(new RectangleF(x + 2f, footY, s - 4f, 2.5f), new ColorRgba(0, 0, 0, 45));
        }
    }

    private static void BakeTileBuildings(
        ChunkMeshBuilder builder,
        int wtx,
        int wty,
        float x,
        float y,
        ChunkWorld2D world,
        float tileSize,
        IVectorArtRepository? art)
    {
        if (!world.Buildings.TryGetStructure(wtx, wty, out StructureTile structure))
        {
            return;
        }

        float s = tileSize;
        if (art is not null && art.TryGetStructure((byte)structure.Type, out CompiledStructureArt? structArt))
        {
            ReadOnlySpan<CompiledVectorShape> shapes = structure.DoorState == DoorState.Open && structArt.OpenShapes.Length > 0
                ? structArt.OpenShapes
                : structArt.Shapes;
            VectorMeshBaker.StampTileShapes(builder, shapes, x, y, s);
        }
        else
        {
            ColorRgba fallbackCol = structure.Category switch
            {
                StructureCategory.Floor => new ColorRgba(150, 130, 100, 255),
                StructureCategory.Wall => new ColorRgba(120, 115, 110, 255),
                StructureCategory.Door => new ColorRgba(135, 95, 60, 255),
                StructureCategory.Pillar => new ColorRgba(120, 125, 135, 255),
                StructureCategory.Station => new ColorRgba(72, 82, 98, 255),
                StructureCategory.Furniture => new ColorRgba(140, 100, 70, 255),
                StructureCategory.None or _ => default,
            };
            if (fallbackCol.A > 0)
            {
                builder.AddRectangle(new RectangleF(x + 1f, y + 1f, s - 2f, s - 2f), fallbackCol);
            }
        }

        if (world.Buildings.IsRoofedAt(wtx, wty))
        {
            builder.AddRectangle(new RectangleF(x, y, s, s), new ColorRgba(20, 30, 45, 35));
        }
    }

    private static ColorRgba HexToColor(uint hex)
    {
        byte r = (byte)((hex >> 24) & 0xFF);
        byte g = (byte)((hex >> 16) & 0xFF);
        byte b = (byte)((hex >> 8) & 0xFF);
        byte a = (byte)(hex & 0xFF);
        return new ColorRgba(r, g, b, a);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            this.ReportBakeStats();
            foreach (ChunkMesh2D mesh in _meshCache.Values)
            {
                mesh.Dispose();
            }

            _meshCache.Clear();
            _whiteTexture.Dispose();
            _shader.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

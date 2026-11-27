// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Desktop.Rendering.World;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;

namespace Carbon.Desktop.Menu;

/// <summary>
/// The living backdrop of the account menus (docs/art/menu-ui.md rule 15): the real generated world around the spawn,
/// drawn with the game's own terrain renderer, slowly panning and dimmed under a dusk-to-night tint, with drifting
/// fireflies. It belongs to <see cref="MenuFlow"/>, so it survives screen changes and the pan never restarts.
/// <list type="bullet">
/// <item>The world and its chunks are generated once on a worker thread, from a generator the session builds with
/// the synced building catalog (<see cref="IGameConfigSource.TryCreateWorldGenerator"/>; the client has no catalog of
/// its own). Before the first config sync, while generating, or if generation fails, the vector night sky
/// (<see cref="MenuBackdropRenderer"/>) shows instead. Input never waits.</item>
/// <item>Chunk meshes are baked one per frame on the render thread, then the terrain fades in.</item>
/// <item>Time only moves while a menu scene drives <see cref="Advance"/>; <see cref="Pause"/> freezes it.</item>
/// <item>Once warm, a frame allocates nothing: the chunk list, camera and batches are reused.</item>
/// </list>
/// </summary>
internal sealed class MenuBackdrop : IDisposable
{
    /// <summary>World rows of the reference view; the zoom keeps this many on screen at any window height.</summary>
    public const float ReferenceViewHeight = 720f;

    private const float FadeSeconds = 0.8f;

    /// <summary>A view wider than 16:9 at the reference height still lands inside the prepared chunks.</summary>
    private const float MaxViewWidth = 1900f;

    /// <summary>How long to wait before asking for a generator again when no catalog was synced yet.</summary>
    private static readonly TimeSpan s_retryDelay = TimeSpan.FromSeconds(1);

    private readonly ulong _seed;
    private readonly Func<ulong, ProceduralWorldGenerator?> _createGenerator;
    private readonly Camera2D _camera = new(1280f, 720f);
    private Task<Prepared?>? _preparing;
    private long _retryAt;
    private bool _noCatalogYet;
    private Prepared? _prepared;
    private ChunkTerrainRenderer? _terrain;
    private int _baked;
    private float _fade;
    private bool _failureLogged;
    private bool _disposed;

    public MenuBackdrop(ulong seed, bool animate, Func<ulong, ProceduralWorldGenerator?> createGenerator)
    {
        ArgumentNullException.ThrowIfNull(createGenerator);
        _seed = seed;
        _createGenerator = createGenerator;
        this.Clock = new MenuBackdropClock(Vector2.Zero, animate);
    }

    /// <summary>Gets the clock (time, pause state, camera path).</summary>
    public MenuBackdropClock Clock { get; }

    /// <summary>
    /// Returns whether the backdrop shows its final look, for screenshots: the world faded fully in, generation failed,
    /// or, when <paramref name="expectWorld"/> is false (no config synced and none coming), the sky.
    /// </summary>
    /// <param name="expectWorld">Whether a synced catalog is on its way (the player is signed in).</param>
    public bool IsSettled(bool expectWorld) => _fade >= 1f || _preparing is { IsFaulted: true } || (_noCatalogYet && !expectWorld);

    /// <summary>Starts generating the world on a worker thread (once). Cheap to call every frame.</summary>
    public void EnsureStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_preparing is null && _prepared is null && Stopwatch.GetTimestamp() >= _retryAt)
        {
            Func<ulong, ProceduralWorldGenerator?> create = _createGenerator;
            ulong seed = _seed;
            _preparing = Task.Run(() => Prepare(create, seed));
        }
    }

    /// <summary>Advances time by <paramref name="dt"/> (the active menu scene calls this from its update).</summary>
    public void Advance(float dt)
    {
        this.Clock.Advance(dt);
        if (_prepared is not null && _baked >= _prepared.Chunks.Length && _fade < 1f)
        {
            _fade = MathF.Min(1f, _fade + (MathF.Max(0f, dt) / FadeSeconds));
        }
    }

    /// <summary>Freezes the backdrop (its scene left or lost focus).</summary>
    public void Pause() => this.Clock.Pause();

    /// <summary>Lets the backdrop move again (a menu scene entered).</summary>
    public void Resume() => this.Clock.Resume();

    /// <summary>Draws the backdrop over the whole viewport; <paramref name="geometry"/> must not be in a batch.</summary>
    public void Render(IGraphicsDevice device, GeometryBatch geometry, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(geometry);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        this.EnsureStarted();
        this.TakePrepared();
        Matrix4x4 screen = Matrix4x4.CreateOrthographicOffCenter(0f, width, height, 0f, -1f, 1f);
        float time = this.Clock.Time;
        if (_prepared is { } world && _fade > 0f)
        {
            this.DrawWorld(device, world, width, height);
        }
        else if (_prepared is { } baking)
        {
            this.BakeNext(device, baking);
        }

        geometry.Begin(screen);
        if (_fade < 1f)
        {
            MenuBackdropRenderer.DrawSky(geometry, width, height, time, 1f - _fade);
        }

        DrawGrade(geometry, width, height, this.Clock.NightAmount, _fade);
        MenuBackdropRenderer.DrawFireflies(geometry, width, height, time);
        geometry.End();
    }

    /// <summary>Releases the GPU meshes (entering the world); the generated chunks stay for a return to the menus.</summary>
    public void ReleaseGpu()
    {
        _terrain?.Dispose();
        _terrain = null;
        _baked = 0;
        _fade = 0f;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        this.ReleaseGpu();
    }

    /// <summary>Generates the world and every chunk the camera loop can see (worker thread, cold path).</summary>
    private static Prepared? Prepare(Func<ulong, ProceduralWorldGenerator?> create, ulong seed)
    {
        if (create(seed) is not { } generator)
        {
            return null;
        }

        ChunkWorld2D world = new(generator);
        (int tileX, int tileY) = MenuBackdropSite.Choose((x, y) => generator.SampleTile(x, y).Biome, world.SpawnTile);
        Vector2 centre = new((tileX + 0.5f) * world.TileSize, (tileY + 0.5f) * world.TileSize);
        MenuBackdropClock path = new(centre, animate: true);
        float chunkWorld = Chunk2D.Size * world.TileSize;
        (int minX, int minY, int maxX, int maxY) = path.ChunkRange(MaxViewWidth, ReferenceViewHeight + 80f, chunkWorld);
        Chunk2D[] chunks = new Chunk2D[(maxX - minX + 1) * (maxY - minY + 1)];
        int n = 0;
        for (int cy = minY; cy <= maxY; cy++)
        {
            for (int cx = minX; cx <= maxX; cx++)
            {
                chunks[n++] = world.GetOrCreateChunk(cx, cy);
            }
        }

        return new Prepared(world, chunks, centre, CarbonVectorArtLibrary.LoadOrDefault());
    }

    private static void DrawGrade(GeometryBatch geometry, int width, int height, float night, float worldAlpha)
    {
        // Dim the world so the panel and text stay the focus (XAG 117), deepening toward night; darker at the top for
        // the title and at the bottom edge, like a vignette.
        if (worldAlpha <= 0f)
        {
            return;
        }

        ColorRgba tint = MenuPalette.Lerp(new ColorRgba(20, 16, 40, 165), new ColorRgba(6, 10, 28, 200), night);
        geometry.FillRectangle(new RectangleF(0f, 0f, width, height), MenuPalette.WithAlpha(tint, worldAlpha));
        const int bands = 24;
        float bandH = MathF.Ceiling(height * 0.3f / bands);
        for (int i = 0; i < bands; i++)
        {
            float a = (1f - (i / (float)bands)) * 0.5f * worldAlpha;
            geometry.FillRectangle(new RectangleF(0f, i * bandH, width, bandH), new ColorRgba(4, 6, 14, (byte)(a * 255f)));
            geometry.FillRectangle(new RectangleF(0f, height - ((i + 1) * bandH * 0.6f), width, bandH * 0.6f), new ColorRgba(4, 6, 14, (byte)(a * 0.8f * 255f)));
        }
    }

    private void TakePrepared()
    {
        if (_prepared is not null || _preparing is null || !_preparing.IsCompleted)
        {
            return;
        }

        if (_preparing.IsCompletedSuccessfully && _preparing.Result is { } prepared)
        {
            _prepared = prepared;
            _noCatalogYet = false;
            this.Clock.Centre = prepared.Centre;
            Console.WriteLine($"[Menu] Backdrop world ready: {prepared.Chunks.Length} chunks around ({prepared.Centre.X:F0}, {prepared.Centre.Y:F0}).");
        }
        else if (_preparing.IsCompletedSuccessfully)
        {
            // No catalog synced yet: keep the sky and ask again shortly (after a sign-in it will be there).
            _noCatalogYet = true;
            _preparing = null;
            _retryAt = Stopwatch.GetTimestamp() + (long)(s_retryDelay.TotalSeconds * Stopwatch.Frequency);
        }
        else if (!_failureLogged)
        {
            _failureLogged = true;
            Console.Error.WriteLine($"[Menu] Backdrop world could not be generated; showing the night sky. {_preparing.Exception?.GetBaseException().Message}");
        }
    }

    private void BakeNext(IGraphicsDevice device, Prepared world)
    {
        if (_baked >= world.Chunks.Length)
        {
            return;
        }

        // Rendering a chunk bakes and caches its mesh; one per frame keeps any hitch short. The sky is drawn over it.
        ChunkTerrainRenderer terrain = _terrain ??= new ChunkTerrainRenderer(device, world.Art);
        Matrix4x4 viewProj = this.ViewProjection(1, 1);
        terrain.Begin(in viewProj);
        terrain.RenderChunk(world.Chunks[_baked], world.World);
        terrain.End();
        _baked++;
    }

    private void DrawWorld(IGraphicsDevice device, Prepared world, int width, int height)
    {
        ChunkTerrainRenderer terrain = _terrain ??= new ChunkTerrainRenderer(device, world.Art);
        Matrix4x4 viewProj = this.ViewProjection(width, height);
        RectangleF visible = _camera.GetVisibleBounds();
        float tileSize = world.World.TileSize;
        terrain.Begin(in viewProj);
        foreach (Chunk2D chunk in world.Chunks)
        {
            if (Overlaps(chunk, tileSize, visible))
            {
                terrain.RenderChunk(chunk, world.World);
            }
        }

        terrain.End();
    }

    private Matrix4x4 ViewProjection(int width, int height)
    {
        _camera.ViewportSize = new Vector2(width, height);
        _camera.Zoom = height / ReferenceViewHeight;
        _camera.Position = this.Clock.Camera;
        return _camera.ViewProjectionMatrix;
    }

    private static bool Overlaps(Chunk2D chunk, float tileSize, in RectangleF view)
    {
        chunk.GetBounds(tileSize, out float minX, out float minY, out float maxX, out float maxY);
        return maxX >= view.X && minX <= view.Right && maxY >= view.Y && minY <= view.Bottom;
    }

    private sealed record Prepared(ChunkWorld2D World, Chunk2D[] Chunks, Vector2 Centre, IVectorArtRepository? Art);
}

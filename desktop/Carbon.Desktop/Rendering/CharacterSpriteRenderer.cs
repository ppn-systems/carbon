// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Carbon.Graphics.Animation.Characters;
using Carbon.Graphics.Animation.Clips;
using Carbon.Graphics.Assets.Loaders;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Device;

namespace Carbon.Desktop.Rendering;

/// <summary>
/// Draws animated characters by visual archetype (<c>human_soldier</c>, <c>slime</c>, ...) from the sprite manifest
/// in <c>resources/characters/character_sprites.json</c>, including wading: the body sinks into shallow water, the
/// submerged part is drawn faint and water-tinted below an animated waterline ring.
/// </summary>
/// <remarks>
/// Sheets are loaded once at start-up. When the art packs are absent (CI, fresh clones) every archetype misses and
/// <see cref="Draw"/> falls back to a procedural token. Drawing allocates nothing: the geometry batch is flushed,
/// the sprite drawn with the shared sprite batch, and geometry resumes, so characters keep their Y-sorted place
/// among props and are darkened by the ambient multiply pass that follows like everything else.
/// </remarks>
internal sealed class CharacterSpriteRenderer : IDisposable
{
    /// <summary>
    /// Fraction of the body (feet to head) below the surface at full wading depth: about chest-deep, face and shoulders above the surface. The art is
    /// chibi-proportioned (the head is nearly half the body), so a larger value would cover the face.
    /// </summary>
    public const float MaxSubmersion = 0.36f;

    private const string ManifestRelativePath = "characters/character_sprites.json";

    private readonly SpriteBatch _sprites;
    private readonly CharacterAnimationLibrary _library;

    private CharacterSpriteRenderer(SpriteBatch sprites, CharacterAnimationLibrary library)
    {
        _sprites = sprites;
        _library = library;
    }

    /// <summary>Gets the number of archetypes whose sheets were found.</summary>
    public int LoadedArchetypes => _library.Count;

    /// <summary>
    /// Loads the manifest and every sheet it names that exists. Never throws for missing or broken art.
    /// </summary>
    public static CharacterSpriteRenderer Create(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        SpriteBatch sprites = new(device);
        CharacterAnimationLibrary library = CharacterAnimationLibrary.Empty;

        string? manifestPath = FindManifest();
        if (manifestPath is null)
        {
            Console.WriteLine("[Characters] Sprite manifest not found; using procedural tokens.");
            return new CharacterSpriteRenderer(sprites, library);
        }

        try
        {
            CharacterSpriteManifest manifest = CharacterSpriteManifest.Parse(File.ReadAllBytes(manifestPath));
            string sheetRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.SheetRoot));
            TextureLoader loader = new(device);
            library = CharacterAnimationLibrary.Build(manifest, sheet => LoadSheet(loader, Path.Combine(sheetRoot, sheet)));
            Console.WriteLine($"[Characters] {library.Count}/{manifest.Archetypes.Count} archetypes loaded ({library.MissingSheets.Count} sheets missing).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"[Characters] Sprite manifest unreadable ({ex.Message}); using procedural tokens.");
        }

        return new CharacterSpriteRenderer(sprites, library);
    }

    /// <summary>
    /// Looks up the animation set of <paramref name="archetype"/>; false when its art is not loaded.
    /// </summary>
    public bool TryGetSet(string archetype, out CharacterAnimationSet set) => _library.TryGet(archetype, out set);

    /// <summary>
    /// Draws one character standing at <paramref name="ground"/>.
    /// </summary>
    /// <param name="geometry">The active world geometry batch (flushed and resumed around the sprite).</param>
    /// <param name="viewProjection">Camera matrix of the current pass.</param>
    /// <param name="set">The archetype's clips, or null for the procedural token.</param>
    /// <param name="state">Playback state.</param>
    /// <param name="ground">Ground contact point in world units (also the water surface when wading).</param>
    /// <param name="tileSize">Tile size in world units (sprite scale reference).</param>
    /// <param name="submersion">Fraction of the body below the water surface, 0 on land.</param>
    /// <param name="velocity">Velocity in world units per second (wake).</param>
    /// <param name="time">Animation time in seconds.</param>
    public void Draw(
        GeometryBatch geometry,
        in Matrix4x4 viewProjection,
        CharacterAnimationSet? set,
        in CharacterAnimationState state,
        Vector2 ground,
        float tileSize,
        float submersion,
        Vector2 velocity,
        float time)
    {
        float wet = Math.Clamp(submersion * 12f, 0f, 1f);
        if (set is null)
        {
            DrawToken(geometry, ground, tileSize, submersion, wet, velocity, time);
            return;
        }

        float scale = set.GetScale(tileSize);
        float halfWidth = set.BodyHeight * scale * 0.3f;
        if (wet > 0f)
        {
            WadingArt.DrawBehind(geometry, ground, halfWidth, wet, velocity, time);
        }
        else
        {
            DrawShadow(geometry, ground, halfWidth);
        }

        geometry.Flush();
        _sprites.Begin(DepthSortMode.Immediate, null, viewProjection);
        this.DrawBody(set, in state, ground, scale, submersion, time);
        _sprites.End();

        if (wet > 0f)
        {
            WadingArt.DrawInFront(geometry, ground, halfWidth, wet, velocity, time);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _library.Dispose();
        _sprites.Dispose();
    }

    private void DrawBody(CharacterAnimationSet set, in CharacterAnimationState state, Vector2 ground, float scale, float submersion, float time)
    {
        AnimationFrame frame = set.GetFrame(in state);
        ITexture texture = frame.Texture;
        RectangleF bounds = frame.Region.Bounds;
        SpriteEffects effects = set.GetEffects(in state);
        Vector2 pivot = set.Pivot;
        float originX = (effects & SpriteEffects.FlipHorizontally) != 0 ? bounds.Width - pivot.X : pivot.X;
        Vector2 scaleVector = new(scale, scale);

        if (submersion <= 0f)
        {
            _sprites.Draw(texture, ground, bounds, ColorRgba.White, 0f, new Vector2(originX, pivot.Y), scaleVector, effects);
            return;
        }

        // The body stands on the bottom, sunk by `sink` sheet pixels: the surface (at `ground`) cuts the frame at
        // `waterline`. Above it the sprite is drawn as is; below it only a faint, tinted, slightly squashed and
        // wobbling remnant shows through the water.
        float sink = MathF.Round(Math.Clamp(submersion, 0f, 1f) * set.BodyHeight);
        float waterline = MathF.Max(1f, pivot.Y - sink);
        RectangleF above = new(bounds.X, bounds.Y, bounds.Width, waterline);
        _sprites.Draw(texture, ground, above, ColorRgba.White, 0f, new Vector2(originX, waterline), scaleVector, effects);

        float belowHeight = MathF.Min(pivot.Y, bounds.Height) - waterline;
        if (belowHeight > 0f)
        {
            RectangleF below = new(bounds.X, bounds.Y + waterline, bounds.Width, belowHeight);
            float wobble = MathF.Sin((time * 4.2f) + ground.Y) * 0.6f;
            Vector2 underwater = new(ground.X + wobble, ground.Y + 1f);
            _sprites.Draw(texture, underwater, below, new ColorRgba(110, 170, 196, 72), 0f, new Vector2(originX, 0f), new Vector2(scale, scale * 0.7f), effects);
        }
    }

    private static void DrawShadow(GeometryBatch geometry, Vector2 ground, float halfWidth)
    {
        Span<Vector2> points = stackalloc Vector2[16];
        for (int i = 0; i < points.Length; i++)
        {
            float a = i * MathF.Tau / points.Length;
            points[i] = ground + new Vector2(MathF.Cos(a) * halfWidth, (MathF.Sin(a) * halfWidth * 0.36f) - 0.5f);
        }

        geometry.FillConvexPolygon(points, new ColorRgba(0, 0, 0, 60));
    }

    // Matte procedural token for when no sprite art is available (CI has no art packs).
    private static void DrawToken(GeometryBatch geometry, Vector2 ground, float tileSize, float submersion, float wet, Vector2 velocity, float time)
    {
        float radius = tileSize * 0.22f;
        Vector2 centre = new(ground.X, ground.Y - radius + (submersion * radius * 1.2f));
        if (wet > 0f)
        {
            WadingArt.DrawBehind(geometry, ground, radius, wet, velocity, time);
        }
        else
        {
            geometry.FillCircle(new Vector2(ground.X + 1.5f, ground.Y), radius * 0.9f, new ColorRgba(0, 0, 0, 70));
        }

        geometry.FillCircle(centre, radius + 1f, new ColorRgba(28, 25, 23, 255));
        geometry.FillCircle(centre, radius, new ColorRgba(55, 65, 110, 255));
        geometry.FillCircle(new Vector2(centre.X, centre.Y - (radius * 0.35f)), radius * 0.45f, new ColorRgba(214, 170, 130, 255));

        if (wet > 0f)
        {
            WadingArt.DrawInFront(geometry, ground, radius, wet, velocity, time);
        }
    }

    private static ITexture? LoadSheet(TextureLoader loader, string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return loader.LoadFromFile(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            Console.WriteLine($"[Characters] Could not load {path}: {ex.Message}");
            return null;
        }
    }

    // CARBON_RESOURCES overrides the resources directory; otherwise search upward from the binary and the working
    // directory (the build output lives under artifacts/bin/...).
    private static string? FindManifest()
    {
        if (Environment.GetEnvironmentVariable("CARBON_RESOURCES") is { Length: > 0 } custom)
        {
            string candidate = Path.Combine(custom, ManifestRelativePath);
            return File.Exists(candidate) ? candidate : null;
        }

        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "resources", ManifestRelativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Graphics.Animation.Atlas;
using Carbon.Graphics.Animation.Clips;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.Animation.Characters;

/// <summary>
/// Registry of <see cref="CharacterAnimationSet"/>s keyed by visual archetype (e.g. <c>human_soldier</c>, <c>slime</c>),
/// built once from a <see cref="CharacterSpriteManifest"/>. Lookups are allocation-free.
/// </summary>
/// <remarks>
/// Sheets that cannot be loaded are skipped (the art packs are optional and git-ignored); an archetype without
/// an idle sheet is not registered, so callers fall back to procedural art for it.
/// </remarks>
public sealed class CharacterAnimationLibrary : IDisposable
{
    private readonly Dictionary<string, CharacterAnimationSet> _sets;
    private readonly List<ITexture> _textures;
    private readonly List<string> _missingSheets;

    private CharacterAnimationLibrary(Dictionary<string, CharacterAnimationSet> sets, List<ITexture> textures, List<string> missingSheets)
    {
        _sets = sets;
        _textures = textures;
        _missingSheets = missingSheets;
    }

    /// <summary>Gets an empty library (every lookup misses).</summary>
    public static CharacterAnimationLibrary Empty => new(new Dictionary<string, CharacterAnimationSet>(StringComparer.Ordinal), [], []);

    /// <summary>Gets the number of loaded archetypes.</summary>
    public int Count => _sets.Count;

    /// <summary>Gets the loaded archetype keys.</summary>
    public IReadOnlyCollection<string> Keys => _sets.Keys;

    /// <summary>Gets the sheet paths that could not be loaded.</summary>
    public IReadOnlyList<string> MissingSheets => _missingSheets;

    /// <summary>
    /// Builds the library from <paramref name="manifest"/>, loading each distinct sheet once.
    /// </summary>
    /// <param name="manifest">Parsed manifest.</param>
    /// <param name="loadSheet">Loads a sheet by its manifest path (relative to the sheet root); returns null when absent.
    /// The library takes ownership of returned textures.</param>
    public static CharacterAnimationLibrary Build(CharacterSpriteManifest manifest, Func<string, ITexture?> loadSheet)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(loadSheet);

        Dictionary<string, CharacterAnimationSet> sets = new(StringComparer.Ordinal);
        Dictionary<string, ITexture?> sheets = new(StringComparer.Ordinal);
        List<ITexture> textures = [];
        List<string> missing = [];

        foreach (CharacterArchetypeDefinition archetype in manifest.Archetypes)
        {
            if (string.IsNullOrEmpty(archetype.Key) || archetype.FrameWidth <= 0 || archetype.FrameHeight <= 0)
            {
                continue;
            }

            AnimationClip?[] clips = new AnimationClip?[CharacterClips.Count];
            foreach (KeyValuePair<string, CharacterClipDefinition> entry in archetype.Clips)
            {
                if (!CharacterClips.TryParse(entry.Key, out CharacterClip clip) || string.IsNullOrEmpty(entry.Value.Sheet))
                {
                    continue;
                }

                if (!sheets.TryGetValue(entry.Value.Sheet, out ITexture? texture))
                {
                    texture = loadSheet(entry.Value.Sheet);
                    sheets.Add(entry.Value.Sheet, texture);
                    if (texture is null)
                    {
                        missing.Add(entry.Value.Sheet);
                    }
                    else
                    {
                        textures.Add(texture);
                    }
                }

                if (texture is not null)
                {
                    clips[(int)clip] = CreateClip(archetype, clip, entry.Value, texture);
                }
            }

            if (clips[(int)CharacterClip.Idle] is not null)
            {
                sets[archetype.Key] = new CharacterAnimationSet(archetype, clips);
            }
        }

        return new CharacterAnimationLibrary(sets, textures, missing);
    }

    /// <summary>
    /// Looks up the animation set of <paramref name="archetype"/>.
    /// </summary>
    public bool TryGet(string archetype, out CharacterAnimationSet set)
    {
        if (archetype is not null && _sets.TryGetValue(archetype, out CharacterAnimationSet? found))
        {
            set = found;
            return true;
        }

        set = null!;
        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (ITexture texture in _textures)
        {
            texture.Dispose();
        }

        _textures.Clear();
        _sets.Clear();
    }

    private static AnimationClip? CreateClip(CharacterArchetypeDefinition archetype, CharacterClip clip, CharacterClipDefinition definition, ITexture texture)
    {
        int cells = (int)(texture.Width / (uint)archetype.FrameWidth);
        int count = definition.Frames > 0 ? Math.Min(definition.Frames, cells) : cells;
        if (count <= 0 || texture.Height < archetype.FrameHeight)
        {
            return null;
        }

        float duration = 1f / MathF.Max(1f, definition.Fps);
        System.Numerics.Vector2 pivot = new(archetype.PivotX, archetype.PivotY);
        AnimationFrame[] frames = new AnimationFrame[count];
        for (int i = 0; i < count; i++)
        {
            RectangleF bounds = new(i * archetype.FrameWidth, 0f, archetype.FrameWidth, archetype.FrameHeight);
            frames[i] = new AnimationFrame(new AtlasRegion(texture, bounds, string.Empty, pivot), duration);
        }

        bool loop = definition.Loop ?? clip.LoopsByDefault();
        return new AnimationClip(clip.ToManifestName(), frames, loop ? AnimationPlaybackMode.Loop : AnimationPlaybackMode.Once);
    }
}

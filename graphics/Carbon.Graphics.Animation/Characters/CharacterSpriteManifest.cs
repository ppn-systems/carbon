// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carbon.Graphics.Animation.Characters;

/// <summary>
/// JSON manifest describing character sprite sheets per visual archetype (see <c>resources/characters/character_sprites.json</c>).
/// </summary>
public sealed class CharacterSpriteManifest
{
    /// <summary>Gets or sets the manifest format version.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Gets or sets the directory, relative to the manifest file, that sheet paths are relative to.</summary>
    public string SheetRoot { get; set; } = ".";

    /// <summary>Gets or sets the archetype definitions.</summary>
    public List<CharacterArchetypeDefinition> Archetypes { get; set; } = [];

    /// <summary>
    /// Parses a UTF-8 manifest (Native-AOT safe: source-generated metadata).
    /// </summary>
    /// <exception cref="JsonException">The payload is not a valid manifest.</exception>
    public static CharacterSpriteManifest Parse(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize(utf8Json, CharacterSpriteManifestJsonContext.Default.CharacterSpriteManifest)
        ?? throw new JsonException("Character sprite manifest is empty.");
}

/// <summary>
/// One visual archetype (e.g. <c>human_soldier</c>, <c>slime</c>): frame geometry and its clips.
/// </summary>
public sealed class CharacterArchetypeDefinition
{
    /// <summary>Gets or sets the archetype key that server definitions refer to.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the frame cell width in sheet pixels.</summary>
    public int FrameWidth { get; set; }

    /// <summary>Gets or sets the frame cell height in sheet pixels.</summary>
    public int FrameHeight { get; set; }

    /// <summary>Gets or sets the X of the ground contact point (between the feet) inside a frame, in pixels.</summary>
    public float PivotX { get; set; }

    /// <summary>Gets or sets the Y of the ground contact point (bottom of the feet) inside a frame, in pixels.</summary>
    public float PivotY { get; set; }

    /// <summary>Gets or sets the drawn body height (feet to top of head) inside a frame, in pixels.</summary>
    public float BodyHeight { get; set; }

    /// <summary>Gets or sets the on-screen body height in tiles; the draw scale is derived from it.</summary>
    public float HeightTiles { get; set; } = 1f;

    /// <summary>Gets or sets whether the sheets face left (they face right otherwise).</summary>
    public bool FacesLeft { get; set; }

    /// <summary>Gets or sets the clips keyed by manifest clip name (<c>idle</c>, <c>walk</c>, <c>attack_melee</c>, ...).</summary>
    public Dictionary<string, CharacterClipDefinition> Clips { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// One clip: a horizontal strip sheet of equally sized frames.
/// </summary>
public sealed class CharacterClipDefinition
{
    /// <summary>Gets or sets the sheet path relative to the manifest's sheet root.</summary>
    public string Sheet { get; set; } = string.Empty;

    /// <summary>Gets or sets the frame count; 0 means every cell of the sheet.</summary>
    public int Frames { get; set; }

    /// <summary>Gets or sets the playback rate in frames per second.</summary>
    public float Fps { get; set; } = 10f;

    /// <summary>Gets or sets whether the clip loops; <see langword="null"/> uses the clip's default (idle and walk loop).</summary>
    public bool? Loop { get; set; }
}

/// <summary>
/// Source-generated JSON metadata for <see cref="CharacterSpriteManifest"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(CharacterSpriteManifest))]
internal sealed partial class CharacterSpriteManifestJsonContext : JsonSerializerContext
{
}

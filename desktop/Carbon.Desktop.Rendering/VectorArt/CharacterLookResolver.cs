// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Contracts.Identity;
using Carbon.Graphics.Batching.Common;
using Carbon.Items.Races;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Turns a <see cref="CharacterLook"/> into what the renderer draws (ADR-042): the body type's rig from
/// <c>races.json</c>, and a <see cref="CharacterPalette"/> built from the race's options by role. Nothing here names a
/// race: a new race in <c>races.json</c> with its rig in <c>catalog.cvg.json</c> renders without a code change.
/// </summary>
public static class CharacterLookResolver
{
    /// <summary>Rig role whose palette recolours skin shapes.</summary>
    public const string SkinRole = "skin";

    /// <summary>Rig role whose palette recolours hair shapes and whose part option picks the hair part.</summary>
    public const string HairRole = "hair";

    /// <summary>Rig role whose palette recolours the iris and whose choice picks the eye style.</summary>
    public const string EyeRole = "eye";

    /// <summary>Resolves the rig and palette for <paramref name="look"/>.</summary>
    /// <param name="art">Loaded vector art.</param>
    /// <param name="races">Synced race catalog, or <see langword="null"/> before the first sync.</param>
    /// <param name="look">Character look.</param>
    /// <param name="rig">The body type's rig, or the art's default character rig when the look names none.</param>
    /// <param name="palette">The palette (<see cref="CharacterPalette.Default"/> where the look names nothing).</param>
    /// <returns><see langword="true"/> when a rig was found.</returns>
    public static bool TryResolve(
        IVectorArtRepository art,
        RaceCatalog? races,
        in CharacterLook look,
        out CompiledVectorRig? rig,
        out CharacterPalette palette)
    {
        ArgumentNullException.ThrowIfNull(art);
        RaceDefinition? race = races?.FindRace(look.RaceId);
        BodyTypeDefinition? body = race?.FindBody(look.BodyType);
        palette = race is null ? CharacterPalette.Default : ResolvePalette(race, in look);
        if (body is not null && art.TryGetCharacterRig(body.Rig, out rig))
        {
            return true;
        }

        rig = art.DefaultCharacterRig;
        return rig is not null;
    }

    /// <summary>Builds the palette of <paramref name="look"/> from the options of <paramref name="race"/>.</summary>
    /// <param name="race">The look's race.</param>
    /// <param name="look">Character look.</param>
    /// <returns>The palette; roles the race does not define keep <see cref="CharacterPalette.Default"/>.</returns>
    public static CharacterPalette ResolvePalette(RaceDefinition race, in CharacterLook look)
    {
        ArgumentNullException.ThrowIfNull(race);
        CharacterPalette p = CharacterPalette.Default;
        List<AppearanceOptionDefinition> options = race.Options;
        for (int i = 0; i < options.Count; i++)
        {
            AppearanceOptionDefinition option = options[i];
            int index = Math.Min(look.GetOption(i), option.Choices.Count - 1);
            AppearanceChoiceDefinition choice = option.Choices[index];
            string role = option.Role;
            if (option.IsPart)
            {
                if (string.Equals(role, HairRole, StringComparison.Ordinal))
                {
                    p = p with { HairPart = (byte)index };
                }

                continue;
            }

            IReadOnlyList<uint> rgb = choice.Rgb;
            if (string.Equals(role, SkinRole, StringComparison.Ordinal))
            {
                p = p with { Skin = ToColor(rgb[0]), SkinShadow = ToColor(rgb.Count > 1 ? rgb[1] : rgb[0]) };
            }
            else if (string.Equals(role, HairRole, StringComparison.Ordinal))
            {
                p = p with { Hair = ToColor(rgb[0]), HairHighlight = ToColor(rgb.Count > 1 ? rgb[1] : rgb[0]) };
            }
            else if (string.Equals(role, EyeRole, StringComparison.Ordinal))
            {
                p = p with { Iris = ToColor(rgb[0]), EyeStyle = (byte)index };
            }
        }

        return p;
    }

    private static ColorRgba ToColor(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
}

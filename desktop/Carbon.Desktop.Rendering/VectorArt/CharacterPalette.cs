// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// The colours and part picks one character look draws with, resolved from <c>races.json</c> by
/// <see cref="CharacterLookResolver"/> (ADR-042). Shapes tagged with a palette role take these colours.
/// </summary>
/// <param name="Skin">Skin base (role <c>skin</c>, first colour).</param>
/// <param name="SkinShadow">Skin shade (role <c>skin</c>, second colour).</param>
/// <param name="Hair">Hair base (role <c>hair</c>, first colour).</param>
/// <param name="HairHighlight">Hair highlight (role <c>hair</c>, second colour).</param>
/// <param name="Iris">Eye iris (role <c>eye</c>).</param>
/// <param name="HairPart">Choice index of the hair part option (role <c>hair</c>).</param>
/// <param name="EyeStyle">Choice index of the eye option (role <c>eye</c>).</param>
public readonly record struct CharacterPalette(
    ColorRgba Skin,
    ColorRgba SkinShadow,
    ColorRgba Hair,
    ColorRgba HairHighlight,
    ColorRgba Iris,
    byte HairPart,
    byte EyeStyle)
{
    /// <summary>Gets the palette drawn before <c>races.json</c> has synced: the first choice of each human option.</summary>
    public static CharacterPalette Default { get; } = new(
        new ColorRgba(245, 198, 165, 255),
        new ColorRgba(212, 155, 120, 255),
        new ColorRgba(28, 32, 46, 255),
        new ColorRgba(68, 76, 102, 255),
        new ColorRgba(30, 58, 138, 255),
        0,
        0);
}

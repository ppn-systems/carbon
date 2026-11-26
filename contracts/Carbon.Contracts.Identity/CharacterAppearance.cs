// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Identity;

/// <summary>
/// Packs and unpacks modular character appearance traits (hair style, hair color, face style, skin tone)
/// into the single-byte <c>AppearancePreset</c> wire field for zero-overhead customization.
/// </summary>
public readonly record struct CharacterAppearance(byte Preset)
{
    /// <summary>Number of available choices per appearance trait.</summary>
    public const byte TraitVariantCount = 4;

    /// <summary>Gets the hair style index (<c>0..3</c>).</summary>
    public byte HairStyle => (byte)(this.Preset & 0x03);

    /// <summary>Gets the hair color palette index (<c>0..3</c>).</summary>
    public byte HairColor => (byte)((this.Preset >> 2) & 0x03);

    /// <summary>Gets the facial expression / eyes style index (<c>0..3</c>).</summary>
    public byte FaceStyle => (byte)((this.Preset >> 4) & 0x03);

    /// <summary>Gets the skin tone index (<c>0..3</c>).</summary>
    public byte SkinTone => (byte)((this.Preset >> 6) & 0x03);

    /// <summary>
    /// Packs the four 2-bit trait indices (<c>0..3</c> each) into a single wire <see cref="byte"/>.
    /// </summary>
    public static byte Pack(byte hairStyle, byte hairColor, byte faceStyle, byte skinTone) =>
        (byte)((hairStyle & 0x03)
            | ((hairColor & 0x03) << 2)
            | ((faceStyle & 0x03) << 4)
            | ((skinTone & 0x03) << 6));
}

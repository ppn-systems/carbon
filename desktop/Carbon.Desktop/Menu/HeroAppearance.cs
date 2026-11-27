// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Identity;

namespace Carbon.Desktop.Menu;

/// <summary>The creator panel's appearance: four traits, each cycling through <see cref="CharacterAppearance.TraitVariantCount"/> variants.</summary>
internal sealed class HeroAppearance
{
    /// <summary>Gets the hair style variant.</summary>
    public byte HairStyle { get; private set; }

    /// <summary>Gets the hair color variant.</summary>
    public byte HairColor { get; private set; }

    /// <summary>Gets the eye (face style) variant.</summary>
    public byte FaceStyle { get; private set; }

    /// <summary>Gets the skin tone variant.</summary>
    public byte SkinTone { get; private set; }

    /// <summary>Gets the packed wire preset (<see cref="CharacterAppearance.Pack"/>).</summary>
    public byte Preset => CharacterAppearance.Pack(this.HairStyle, this.HairColor, this.FaceStyle, this.SkinTone);

    /// <summary>Moves the hair style by <paramref name="step"/> (±1), wrapping.</summary>
    public void CycleHairStyle(int step) => this.HairStyle = Cycle(this.HairStyle, step);

    /// <summary>Moves the hair color by <paramref name="step"/> (±1), wrapping.</summary>
    public void CycleHairColor(int step) => this.HairColor = Cycle(this.HairColor, step);

    /// <summary>Moves the eye variant by <paramref name="step"/> (±1), wrapping.</summary>
    public void CycleFaceStyle(int step) => this.FaceStyle = Cycle(this.FaceStyle, step);

    /// <summary>Moves the skin tone by <paramref name="step"/> (±1), wrapping.</summary>
    public void CycleSkinTone(int step) => this.SkinTone = Cycle(this.SkinTone, step);

    /// <summary>Picks every trait at random.</summary>
    public void Randomize(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        this.HairStyle = (byte)random.Next(CharacterAppearance.TraitVariantCount);
        this.HairColor = (byte)random.Next(CharacterAppearance.TraitVariantCount);
        this.FaceStyle = (byte)random.Next(CharacterAppearance.TraitVariantCount);
        this.SkinTone = (byte)random.Next(CharacterAppearance.TraitVariantCount);
    }

    private static byte Cycle(byte value, int step) =>
        (byte)((value + step + CharacterAppearance.TraitVariantCount) % CharacterAppearance.TraitVariantCount);
}

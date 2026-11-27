// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Generators;

/// <summary>
/// Everything that defines a generated world. The server builds it once and sends it to clients in
/// <c>CharacterEnterWorld</c> (WorldSeed, TileSize, Frequency, GeneratorVersion); both sides then generate the same world.
/// </summary>
/// <param name="Seed">64-bit world seed.</param>
/// <param name="Frequency">Macro noise frequency (continent scale).</param>
/// <param name="TileSize">World units per tile.</param>
public sealed record WorldGenSettings(
    ulong Seed,
    float Frequency = ProceduralWorldGenerator.DefaultFrequency,
    float TileSize = WorldGenSettings.DefaultTileSize)
{
    /// <summary>Default world units per tile.</summary>
    public const float DefaultTileSize = 32f;

    /// <summary>Gets the generator version these settings are interpreted with.</summary>
    public static ushort GeneratorVersion => ProceduralWorldGenerator.GeneratorVersion;

    /// <summary>Validates the settings.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is not positive and finite.</exception>
    public void Validate()
    {
        if (!float.IsFinite(this.Frequency) || this.Frequency <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(this.Frequency), this.Frequency, "Frequency must be positive.");
        }

        if (!float.IsFinite(this.TileSize) || this.TileSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(this.TileSize), this.TileSize, "Tile size must be positive.");
        }
    }
}

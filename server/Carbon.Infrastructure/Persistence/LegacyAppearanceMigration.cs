// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Identity;

namespace Carbon.Infrastructure.Persistence;

/// <summary>
/// Migration 0022 (ADR-042): turns the retired packed <c>appearance_preset</c> byte into a structured look. Every legacy
/// character becomes a human (race 1) with the male body (body type 0), and its four 2-bit traits map onto the human
/// options of <c>races.json</c> in their stored order: hair style, hair color, eye color, skin tone.
/// </summary>
/// <remarks>
/// The retired layout was <c>hairStyle | hairColor &lt;&lt; 2 | faceStyle &lt;&lt; 4 | skinTone &lt;&lt; 6</c>, two bits each.
/// This mapping is frozen: it describes data written before protocol 8, not the current <c>races.json</c>.
/// </remarks>
public static class LegacyAppearanceMigration
{
    /// <summary>Migration id recorded in <c>schema_migrations</c>.</summary>
    public const string MigrationId = "0022_character_look";

    /// <summary>Race id every legacy character becomes (human).</summary>
    public const ushort LegacyRaceId = 1;

    /// <summary>Body type id every legacy character becomes (male).</summary>
    public const byte LegacyBodyType = 0;

    /// <summary>Converts a retired packed appearance byte to its look.</summary>
    /// <param name="preset">The stored <c>appearance_preset</c> value.</param>
    /// <returns>The human, male-bodied look with the same traits.</returns>
    public static CharacterLook ToLook(byte preset)
    {
        ReadOnlySpan<byte> options =
        [
            (byte)(preset & 0x03), //        hair_style
            (byte)((preset >> 2) & 0x03), // hair_color
            (byte)((preset >> 4) & 0x03), // eye_color (was face style, which picked the iris colour)
            (byte)((preset >> 6) & 0x03), // skin_tone
        ];
        return new CharacterLook(LegacyRaceId, LegacyBodyType, options);
    }
}

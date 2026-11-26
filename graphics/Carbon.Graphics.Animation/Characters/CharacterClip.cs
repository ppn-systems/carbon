// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.Animation.Characters;

/// <summary>
/// Logical animation clips every character archetype may provide. Gameplay state (movement, attack phases,
/// damage, death) selects one of these; the archetype's manifest maps it to a sprite sheet.
/// </summary>
public enum CharacterClip
{
    /// <summary>Standing still (manifest name <c>idle</c>). Required for every archetype.</summary>
    Idle = 0,

    /// <summary>Moving (manifest name <c>walk</c>).</summary>
    Walk = 1,

    /// <summary>Melee swing, played across windup/active/recovery (manifest name <c>attack_melee</c>).</summary>
    AttackMelee = 2,

    /// <summary>Ranged shot, played across aim/release (manifest name <c>attack_ranged</c>).</summary>
    AttackRanged = 3,

    /// <summary>Spell, played across channel/detonate (manifest name <c>cast</c>).</summary>
    Cast = 4,

    /// <summary>Taking damage (manifest name <c>hurt</c>).</summary>
    Hurt = 5,

    /// <summary>Dying; holds the last frame (manifest name <c>death</c>).</summary>
    Death = 6,
}

/// <summary>
/// Manifest names and fallbacks for <see cref="CharacterClip"/>.
/// </summary>
public static class CharacterClips
{
    /// <summary>Number of <see cref="CharacterClip"/> values.</summary>
    public const int Count = 7;

    /// <summary>
    /// Gets the manifest key of <paramref name="clip"/> (snake_case).
    /// </summary>
    public static string ToManifestName(this CharacterClip clip) => clip switch
    {
        CharacterClip.Idle => "idle",
        CharacterClip.Walk => "walk",
        CharacterClip.AttackMelee => "attack_melee",
        CharacterClip.AttackRanged => "attack_ranged",
        CharacterClip.Cast => "cast",
        CharacterClip.Hurt => "hurt",
        CharacterClip.Death => "death",
        _ => "idle",
    };

    /// <summary>
    /// Parses a manifest clip key.
    /// </summary>
    /// <returns><see langword="true"/> when <paramref name="name"/> names a known clip.</returns>
    public static bool TryParse(string? name, out CharacterClip clip)
    {
        for (int i = 0; i < Count; i++)
        {
            if (string.Equals(((CharacterClip)i).ToManifestName(), name, System.StringComparison.Ordinal))
            {
                clip = (CharacterClip)i;
                return true;
            }
        }

        clip = CharacterClip.Idle;
        return false;
    }

    /// <summary>
    /// Gets the clip to use when an archetype lacks <paramref name="clip"/>: ranged and cast fall back to melee,
    /// melee, hurt and walk fall back to idle. Idle and death have no fallback (returns the clip itself).
    /// </summary>
    public static CharacterClip Fallback(this CharacterClip clip) => clip switch
    {
        CharacterClip.AttackRanged or CharacterClip.Cast => CharacterClip.AttackMelee,
        CharacterClip.AttackMelee or CharacterClip.Hurt or CharacterClip.Walk => CharacterClip.Idle,
        CharacterClip.Idle or CharacterClip.Death => clip,
        _ => clip,
    };

    /// <summary>
    /// Gets whether <paramref name="clip"/> repeats by default (idle, walk) rather than playing once.
    /// </summary>
    public static bool LoopsByDefault(this CharacterClip clip) => clip is CharacterClip.Idle or CharacterClip.Walk;
}

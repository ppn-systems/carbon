// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Combat.Statuses.Config;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Feedback;

/// <summary>Damage-number colours for status effect pulses (ADR-029 D10), chosen by the effect's key.</summary>
internal static class StatusEffectTints
{
    /// <summary>Poison pulses: sickly green.</summary>
    public static readonly ColorRgba Poison = new(132, 204, 22, 255);

    /// <summary>Bleeding pulses: dark crimson.</summary>
    public static readonly ColorRgba Bleed = new(220, 38, 80, 255);

    /// <summary>Burning pulses and fire telegraphs: orange (ADR-036 D9).</summary>
    public static readonly ColorRgba Burn = new(249, 115, 22, 255);

    /// <summary>Frostbite pulses and frost telegraphs: ice blue (ADR-036 D9).</summary>
    public static readonly ColorRgba Frost = new(125, 211, 252, 255);

    /// <summary>Weakness telegraphs: violet.</summary>
    public static readonly ColorRgba Weakness = new(167, 139, 250, 255);

    /// <summary>Blindness telegraphs: ash grey.</summary>
    public static readonly ColorRgba Blind = new(148, 163, 184, 255);

    /// <summary>Slow telegraphs: cold blue.</summary>
    public static readonly ColorRgba Slow = new(96, 165, 250, 255);

    /// <summary>Stun telegraphs: amber.</summary>
    public static readonly ColorRgba Stun = new(251, 191, 36, 255);

    /// <summary>Any other effect's pulse.</summary>
    public static readonly ColorRgba Other = new(196, 160, 255, 255);

    /// <summary>Gets the tint for a pulse of <paramref name="effectId"/>.</summary>
    /// <param name="catalog">Synced status effect definitions.</param>
    /// <param name="effectId">Effect id from <c>status_effects.json</c>.</param>
    /// <returns>The tint.</returns>
    public static ColorRgba ForEffect(StatusEffectCatalog catalog, ushort effectId)
    {
        System.ArgumentNullException.ThrowIfNull(catalog);
        if (!catalog.TryGet(effectId, out StatusEffectDefinition def))
        {
            return Other;
        }

        return def.Key switch
        {
            "poisoned" => Poison,
            "bleeding" => Bleed,
            "burning" => Burn,
            "frostbite" => Frost,
            _ => Other,
        };
    }

    /// <summary>Gets the telegraph tint of a monster whose attack applies <paramref name="effectKey"/> (ADR-036 D10).</summary>
    /// <param name="effectKey">Effect key from <c>status_effects.json</c>.</param>
    /// <returns>The tint, or <see langword="null"/> to keep the attack kind's colour.</returns>
    public static ColorRgba? ForTelegraph(string effectKey) => effectKey switch
    {
        "poisoned" => Poison,
        "bleeding" => Bleed,
        "burning" => Burn,
        "frostbite" => Frost,
        "weakness" => Weakness,
        "blindness" => Blind,
        "slowed" => Slow,
        "stunned" => Stun,
        _ => null,
    };
}

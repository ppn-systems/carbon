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
            _ => Other,
        };
    }
}

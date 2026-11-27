// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Items.Repair;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// The colour of a durability bar or line (ADR-035): green while healthy, yellow at or below the low share and red at
/// or below the critical share of the synced <c>repair.json → durabilityWarning</c>, so the inventory, hotbar and
/// tooltip warn before an item breaks.
/// </summary>
internal static class DurabilityBarStyle
{
    /// <summary>Returns the fill colour of a bar at <paramref name="durability"/> out of <paramref name="maxDurability"/>.</summary>
    /// <param name="repair">The synced repair table (warning shares).</param>
    /// <param name="durability">Current durability.</param>
    /// <param name="maxDurability">Effective maximum durability.</param>
    /// <returns>The bar colour.</returns>
    public static ColorRgba GetFill(RepairTable repair, float durability, float maxDurability) =>
        repair.GetDurabilityBand(durability, maxDurability) switch
        {
            DurabilityBand.Critical => CarbonUiPalette.HpFill,
            DurabilityBand.Low => CarbonUiPalette.DurabilityLowFill,
            DurabilityBand.Healthy or _ => CarbonUiPalette.StaminaFill,
        };

    /// <summary>Returns whether an item is at or below the critical share (the hotbar outlines it red).</summary>
    /// <param name="repair">The synced repair table (warning shares).</param>
    /// <param name="durability">Current durability.</param>
    /// <param name="maxDurability">Effective maximum durability (0 for an item without durability).</param>
    /// <returns><see langword="true"/> when the item is about to break.</returns>
    public static bool IsCritical(RepairTable repair, float durability, float maxDurability) =>
        repair.GetDurabilityBand(durability, maxDurability) == DurabilityBand.Critical;
}

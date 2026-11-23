// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.Batching.Sprites;

/// <summary>
/// Computes sorting keys for 2D/2.5D sprites according to the active depth sort mode.
/// </summary>
public static class SpriteSortKey
{
    private const float LAYER_MULTIPLIER = 100000.0f;

    /// <summary>
    /// Calculates the 32-bit floating-point sort key used to order sprites before GPU submission.
    /// </summary>
    /// <param name="sortMode">The active sorting mode.</param>
    /// <param name="footY">The Y position of the character's feet or object base in world space.</param>
    /// <param name="layer">The explicit rendering layer index.</param>
    /// <param name="depth">The depth / Z offset.</param>
    /// <param name="textureHandle">The GPU texture handle ID.</param>
    /// <returns>A floating-point sort key for order comparison.</returns>
    public static float Calculate(DepthSortMode sortMode, float footY, int layer, float depth, uint textureHandle) => sortMode switch
    {
        DepthSortMode.YSorted => (layer * LAYER_MULTIPLIER) + footY + (depth * 0.01f),
        DepthSortMode.BackToFront => (layer * LAYER_MULTIPLIER) + depth,
        DepthSortMode.Immediate => 0f,
        _ => textureHandle
    };
}

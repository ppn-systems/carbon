// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Crafting;

namespace Carbon.Items.Enhancement;

/// <summary>
/// One compiled attempt row: from <see cref="FromLevel"/> to <c>FromLevel + 1</c>. Weights are per mille and sum to
/// <see cref="EnhancementTable.WeightTotal"/>.
/// </summary>
/// <param name="FromLevel">Current item level.</param>
/// <param name="SuccessPermille">Chance to gain a level.</param>
/// <param name="NoChangePermille">Chance to keep the level.</param>
/// <param name="DowngradePermille">Chance to lose a level.</param>
/// <param name="DestroyPermille">Chance to destroy the item.</param>
/// <param name="BaseGold">Gold fee before the rarity multiplier.</param>
/// <param name="Materials">Materials spent on every attempt.</param>
public sealed record EnhancementStep(
    byte FromLevel,
    int SuccessPermille,
    int NoChangePermille,
    int DowngradePermille,
    int DestroyPermille,
    long BaseGold,
    RecipeIngredient[] Materials)
{
    /// <summary>Gets the materials as a span.</summary>
    public ReadOnlySpan<RecipeIngredient> MaterialSpan => this.Materials;

    /// <summary>Gets whether a failure here can cost the item anything (a level or the item).</summary>
    public bool IsRisky => this.DowngradePermille > 0 || this.DestroyPermille > 0;
}

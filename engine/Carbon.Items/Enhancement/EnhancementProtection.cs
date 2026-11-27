// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Enhancement;

/// <summary>A compiled protection scroll.</summary>
/// <param name="ItemId">Scroll item id.</param>
/// <param name="PreventsDowngrade">A downgrade becomes no change.</param>
/// <param name="PreventsDestroy">Destruction becomes a downgrade (or no change with <paramref name="PreventsDowngrade"/>).</param>
public readonly record struct EnhancementProtection(uint ItemId, bool PreventsDowngrade, bool PreventsDestroy)
{
    /// <summary>Returns whether this scroll can change any failure of <paramref name="step"/>.</summary>
    /// <param name="step">Attempt row.</param>
    /// <returns><see langword="true"/> when spending it on that step could matter.</returns>
    public bool AppliesTo(EnhancementStep step) =>
        step is not null && ((this.PreventsDowngrade && step.DowngradePermille > 0) || (this.PreventsDestroy && step.DestroyPermille > 0));
}

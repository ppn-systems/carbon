// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Items.Progression;

/// <summary>The experience curve and the kill experience formula.</summary>
public sealed class ExperienceConfig
{
    /// <summary>Gets the experience curve anchors, strictly increasing in level and experience.</summary>
    public IReadOnlyList<XpAnchor> XpToNext { get; init; } = [];

    /// <summary>Gets the kill experience formula.</summary>
    public KillExperienceConfig Kill { get; init; } = new();
}

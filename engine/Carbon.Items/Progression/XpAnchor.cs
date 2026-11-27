// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Progression;

/// <summary>
/// One point of the experience curve: <see cref="Xp"/> is the experience needed to go from <see cref="Level"/> to the
/// next level. Levels between anchors interpolate geometrically.
/// </summary>
public sealed class XpAnchor
{
    /// <summary>Gets the level.</summary>
    public int Level { get; init; }

    /// <summary>Gets the experience needed to leave <see cref="Level"/>.</summary>
    public long Xp { get; init; }
}

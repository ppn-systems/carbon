// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Progression;

/// <summary>Attribute points granted on reaching each level in <c>[FromLevel, ToLevel]</c>.</summary>
public sealed class StatPointBracket
{
    /// <summary>Gets the first level of the bracket (inclusive).</summary>
    public int FromLevel { get; init; }

    /// <summary>Gets the last level of the bracket (inclusive).</summary>
    public int ToLevel { get; init; }

    /// <summary>Gets the points granted on reaching each level of the bracket.</summary>
    public int PointsPerLevel { get; init; }
}

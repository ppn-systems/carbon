// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>An inclusive numeric design range.</summary>
public sealed record ValueRange
{
    /// <summary>Gets or sets the lower bound.</summary>
    public double Min { get; set; }

    /// <summary>Gets or sets the upper bound.</summary>
    public double Max { get; set; }
}

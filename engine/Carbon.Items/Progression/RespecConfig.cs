// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Progression;

/// <summary>Attribute reset price: free through <see cref="FreeThroughLevel"/>, then <c>BaseGold + GoldPerLevel * level</c>.</summary>
public sealed class RespecConfig
{
    /// <summary>Gets the last level at which a reset is free.</summary>
    public int FreeThroughLevel { get; init; } = 10;

    /// <summary>Gets the flat gold part of the price.</summary>
    public long BaseGold { get; init; } = 50;

    /// <summary>Gets the gold per character level.</summary>
    public long GoldPerLevel { get; init; } = 20;
}

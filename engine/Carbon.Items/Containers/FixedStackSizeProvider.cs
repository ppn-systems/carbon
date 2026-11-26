// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Items.Containers;

/// <summary>
/// <see cref="IStackSizeProvider"/> returning the same stack size for every item.
/// </summary>
public sealed class FixedStackSizeProvider : IStackSizeProvider
{
    private readonly int _maxStack;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedStackSizeProvider"/> class.
    /// </summary>
    /// <param name="maxStack">Stack size returned for every item.</param>
    public FixedStackSizeProvider(int maxStack)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStack);
        _maxStack = maxStack;
    }

    /// <inheritdoc/>
    public int GetMaxStack(uint itemId) => _maxStack;
}

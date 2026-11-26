// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Containers;

/// <summary>
/// Resolves the maximum stack size of an item definition for capacity checks.
/// </summary>
public interface IStackSizeProvider
{
    /// <summary>
    /// Gets the maximum quantity of <paramref name="itemId"/> that fits in one slot (at least <c>1</c>).
    /// </summary>
    /// <param name="itemId">Item definition identifier.</param>
    /// <returns>Maximum stack size.</returns>
    int GetMaxStack(uint itemId);
}

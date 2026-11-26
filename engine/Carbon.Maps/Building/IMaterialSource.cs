// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Building;

/// <summary>
/// Port receiving refunded or dropped construction materials (a player inventory, or ground loot at the site).
/// Keeps <c>Carbon.Maps</c> independent of the items module.
/// </summary>
public interface IMaterialSink
{
    /// <summary>Hands materials to the sink. Implementations decide what to do with overflow (e.g. drop on the ground).</summary>
    /// <param name="itemId">Item id string.</param>
    /// <param name="quantity">Units given (always positive).</param>
    void Add(string itemId, int quantity);
}

/// <summary>
/// Port over a builder's inventory used to deliver construction materials.
/// </summary>
public interface IMaterialSource : IMaterialSink
{
    /// <summary>Counts the units of an item available.</summary>
    /// <param name="itemId">Item id string.</param>
    /// <returns>Units available.</returns>
    int CountOf(string itemId);

    /// <summary>Removes up to <paramref name="quantity"/> units of an item.</summary>
    /// <param name="itemId">Item id string.</param>
    /// <param name="quantity">Units requested (always positive).</param>
    /// <returns>Units actually removed.</returns>
    int Take(string itemId, int quantity);
}

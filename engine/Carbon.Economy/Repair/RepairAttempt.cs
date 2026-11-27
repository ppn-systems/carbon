// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Economy.Repair;

/// <summary>
/// What one repair did (ADR-028). When <see cref="Error"/> is not <see cref="RepairError.None"/> nothing was spent and
/// nothing changed.
/// </summary>
/// <param name="Error"><see cref="RepairError.None"/> once the repair ran; otherwise why it was refused.</param>
/// <param name="ItemId">The repaired item.</param>
/// <param name="Steps">Steps bought.</param>
/// <param name="Durability">Durability after the repair.</param>
/// <param name="MaxDurability">The item's maximum durability.</param>
/// <param name="GoldSpent">Blacksmith fee paid.</param>
public readonly record struct RepairAttempt(RepairError Error, uint ItemId, int Steps, float Durability, float MaxDurability, long GoldSpent)
{
    /// <summary>Gets whether the repair ran.</summary>
    public bool IsOk => this.Error == RepairError.None;

    /// <summary>Builds a refusal.</summary>
    /// <param name="error">Why.</param>
    /// <returns>The refused attempt.</returns>
    public static RepairAttempt Refused(RepairError error) => new(error, 0U, 0, 0f, 0f, 0L);
}

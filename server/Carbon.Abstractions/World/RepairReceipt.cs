// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Abstractions.World;

/// <summary>
/// The outcome of one tick-queued repair (ADR-028), carried back to the handler in
/// <see cref="WorldMutationOutcome.Revision"/> (<see cref="Pack"/>/<see cref="Unpack"/>) so the command queue stays
/// allocation-free.
/// </summary>
/// <param name="Error">Why the repair was refused (<see cref="RepairError.None"/> when it ran).</param>
/// <param name="Steps">Steps bought.</param>
public readonly record struct RepairReceipt(RepairError Error, byte Steps)
{
    /// <summary>Packs the receipt into 32 bits: error (8), steps (8).</summary>
    /// <returns>The packed value.</returns>
    public uint Pack() => (byte)this.Error | ((uint)this.Steps << 8);

    /// <summary>Unpacks a value made by <see cref="Pack"/>.</summary>
    /// <param name="packed">Packed value.</param>
    /// <returns>The receipt.</returns>
    public static RepairReceipt Unpack(uint packed) => new((RepairError)(packed & 0xFF), (byte)((packed >> 8) & 0xFF));
}

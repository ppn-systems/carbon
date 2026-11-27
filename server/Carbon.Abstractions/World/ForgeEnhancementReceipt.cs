// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Abstractions.World;

/// <summary>
/// The outcome of one tick-queued forge enhancement attempt (ADR-024), carried back to the handler in
/// <see cref="WorldMutationOutcome.Revision"/> (<see cref="Pack"/>/<see cref="Unpack"/>) so the command queue stays
/// allocation-free.
/// </summary>
/// <param name="Outcome">Rolled outcome (<see cref="EnhancementOutcome.None"/> when the attempt was rejected).</param>
/// <param name="ItemId">The item that was enhanced.</param>
/// <param name="PreviousLevel">Level before the attempt (<c>0..15</c>).</param>
/// <param name="NewLevel">Level after the attempt (<c>0..15</c>, <c>0</c> when destroyed).</param>
/// <param name="ProtectionConsumed">Whether a protection scroll was spent.</param>
public readonly record struct ForgeEnhancementReceipt(
    EnhancementOutcome Outcome,
    ushort ItemId,
    byte PreviousLevel,
    byte NewLevel,
    bool ProtectionConsumed)
{
    /// <summary>Packs the receipt into 32 bits: item id (16), outcome (4), previous (4), new (4), protection (1).</summary>
    /// <returns>The packed value.</returns>
    public uint Pack() =>
        this.ItemId
        | ((uint)((byte)this.Outcome & 0x0F) << 16)
        | ((uint)(this.PreviousLevel & 0x0F) << 20)
        | ((uint)(this.NewLevel & 0x0F) << 24)
        | (this.ProtectionConsumed ? 1U << 28 : 0U);

    /// <summary>Unpacks a value made by <see cref="Pack"/>.</summary>
    /// <param name="packed">Packed value.</param>
    /// <returns>The receipt.</returns>
    public static ForgeEnhancementReceipt Unpack(uint packed) => new(
        (EnhancementOutcome)((packed >> 16) & 0x0F),
        (ushort)(packed & 0xFFFF),
        (byte)((packed >> 20) & 0x0F),
        (byte)((packed >> 24) & 0x0F),
        ((packed >> 28) & 1U) != 0U);
}

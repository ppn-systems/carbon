// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Abstractions.World;

/// <summary>
/// The outcome of one tick-queued trader basket or cart (ADR-025 D18), carried back to the handler in
/// <see cref="WorldMutationOutcome.Revision"/> (<see cref="Pack"/>/<see cref="Unpack"/>), like <see cref="RepairReceipt"/>.
/// </summary>
/// <param name="Error">Why the batch was refused (<see cref="TradeBatchError.None"/> when it was applied).</param>
/// <param name="Line">Index of the first failing line, or <see cref="byte.MaxValue"/>.</param>
public readonly record struct TradeBatchReceipt(TradeBatchError Error, byte Line)
{
    /// <summary>Packs the receipt into 32 bits: error (8), line (8).</summary>
    /// <returns>The packed value.</returns>
    public uint Pack() => (byte)this.Error | ((uint)this.Line << 8);

    /// <summary>Unpacks a value made by <see cref="Pack"/>.</summary>
    /// <param name="packed">Packed value.</param>
    /// <returns>The receipt.</returns>
    public static TradeBatchReceipt Unpack(uint packed) => new((TradeBatchError)(packed & 0xFF), (byte)((packed >> 8) & 0xFF));
}

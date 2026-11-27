// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Repairing one item (ADR-028), as the client sees it: it sends the request and keeps the server's last answer for the
/// repair window's animation. The server checks and spends everything; the client never decides a repair.
/// </summary>
public interface IRepairSession
{
    /// <summary>Gets whether a request is waiting for the server's answer.</summary>
    bool IsPending { get; }

    /// <summary>Gets a counter that increases with every answer, so the window starts one result effect per answer.</summary>
    uint ResultSerial { get; }

    /// <summary>Gets the server's last answer.</summary>
    RepairResultView LastResult { get; }

    /// <summary>Asks the server to repair <paramref name="steps"/> steps of the item in <paramref name="slotIndex"/> at <paramref name="venue"/>.</summary>
    /// <param name="slotIndex">Bag or equipment slot.</param>
    /// <param name="expectedItemId">Item the window shows in the slot.</param>
    /// <param name="steps">Steps to buy.</param>
    /// <param name="venue">Station or blacksmith.</param>
    /// <returns><see langword="false"/> when nothing was sent (not connected, a request already pending, or no steps).</returns>
    bool TryRepair(ushort slotIndex, ushort expectedItemId, byte steps, RepairVenue venue);
}

/// <summary>The server's answer to one repair request (ADR-028).</summary>
/// <param name="Status">Queue or world status.</param>
/// <param name="Error">Why the repair was refused (<see cref="RepairError.None"/> on success).</param>
/// <param name="SlotIndex">The slot the request named.</param>
/// <param name="Steps">Steps bought.</param>
public readonly record struct RepairResultView(OpStatus Status, RepairError Error, ushort SlotIndex, byte Steps)
{
    /// <summary>Gets whether the repair ran.</summary>
    public bool IsOk => this.Status == OpStatus.Ok && this.Error == RepairError.None;
}

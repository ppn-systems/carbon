// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Economy.Enhancement;

/// <summary>
/// What one forge enhancement attempt did (ADR-020). When <see cref="Status"/> is not <see cref="OpStatus.Ok"/> nothing
/// was spent and nothing changed.
/// </summary>
/// <param name="Status"><see cref="OpStatus.Ok"/> once the attempt ran; otherwise why it was rejected.</param>
/// <param name="Outcome">The rolled outcome (after protection).</param>
/// <param name="ItemId">The item that was enhanced.</param>
/// <param name="PreviousLevel">Level before the attempt.</param>
/// <param name="NewLevel">Level after the attempt (<c>0</c> when destroyed).</param>
/// <param name="GoldSpent">Gold fee paid.</param>
/// <param name="ProtectionConsumed">Whether a protection scroll was spent.</param>
public readonly record struct ForgeAttempt(
    OpStatus Status,
    EnhancementOutcome Outcome,
    uint ItemId,
    byte PreviousLevel,
    byte NewLevel,
    long GoldSpent,
    bool ProtectionConsumed)
{
    /// <summary>Gets whether the item was destroyed.</summary>
    public bool IsDestroyed => this.Outcome == EnhancementOutcome.Destroyed;

    /// <summary>Builds a rejection.</summary>
    /// <param name="status">Why.</param>
    /// <returns>The rejected attempt.</returns>
    public static ForgeAttempt Rejected(OpStatus status) => new(status, EnhancementOutcome.None, 0U, 0, 0, 0L, false);
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;

namespace Carbon.Host.Tests;

/// <summary>Test shorthand over <see cref="AuthoritativeWorld.TryEnhanceInventoryItem"/> (ADR-024).</summary>
internal static class ForgeWorldTestExtensions
{
    /// <summary>Runs one unprotected attempt: <see cref="OpStatus.Ok"/> on a success, <see cref="OpStatus.EnhancementFailed"/> on a failed roll.</summary>
    public static OpStatus TryUpgradeInventoryItem(this AuthoritativeWorld world, ulong connectionId, ushort slotIndex, uint deterministicRollSeed = 0U)
    {
        WorldMutationOutcome outcome = world.TryEnhanceInventoryItem(connectionId, slotIndex, protectionItemId: 0, deterministicRollSeed);
        return outcome.Status != OpStatus.Ok || ForgeEnhancementReceipt.Unpack(outcome.Revision).Outcome == EnhancementOutcome.Success
            ? outcome.Status
            : OpStatus.EnhancementFailed;
    }
}

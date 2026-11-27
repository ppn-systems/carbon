// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using System.Reflection;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Nalix.Abstractions.Serialization;
using Xunit;

namespace Carbon.Contracts.Tests;

/// <summary>Wire contracts of forge enhancement (ADR-024).</summary>
public sealed class EnhancementPacketTests
{
    [Fact]
    public void UpgradeInventoryItem_Request_CarriesOnlyTheSlotAndTheScroll()
    {
        // The client names what to enhance and which scroll to spend; it has no field to choose, seed or report an outcome.
        string[] fields = [.. typeof(UpgradeInventoryItem)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static p => p.GetCustomAttribute<SerializeOrderAttribute>() is not null)
            .OrderBy(static p => p.GetCustomAttribute<SerializeOrderAttribute>()!.Order)
            .Select(static p => p.Name)];

        Assert.Equal(["SlotIndex", "ProtectionItemId"], fields);

        using UpgradeInventoryItem request = new() { SlotIndex = 37, ProtectionItemId = 206 };
        using UpgradeInventoryItem copy = UpgradeInventoryItem.Deserialize(request.Serialize());
        Assert.Equal((ushort)37, copy.SlotIndex);
        Assert.Equal((ushort)206, copy.ProtectionItemId);
    }

    [Fact]
    public void EnhancementResultAndAnnouncement_RoundTrip_KeepEveryField()
    {
        using EnhancementResult result = new()
        {
            Status = OpStatus.Ok,
            ItemId = 183,
            Outcome = EnhancementOutcome.Destroyed,
            NewLevel = 0,
            ProtectionConsumed = false,
        };
        using EnhancementResult copy = EnhancementResult.Deserialize(result.Serialize());
        Assert.Equal(OpStatus.Ok, copy.Status);
        Assert.Equal((ushort)183, copy.ItemId);
        Assert.Equal(EnhancementOutcome.Destroyed, copy.Outcome);
        Assert.Equal((byte)0, copy.NewLevel);
        Assert.False(copy.ProtectionConsumed);

        using EnhancementAnnouncement notice = new() { PlayerName = "Smith", ItemId = 31, Outcome = EnhancementOutcome.Success, Level = 8 };
        using EnhancementAnnouncement noticeCopy = EnhancementAnnouncement.Deserialize(notice.Serialize());
        Assert.Equal("Smith", noticeCopy.PlayerName);
        Assert.Equal((ushort)31, noticeCopy.ItemId);
        Assert.Equal(EnhancementOutcome.Success, noticeCopy.Outcome);
        Assert.Equal((byte)8, noticeCopy.Level);
    }

    [Fact]
    public void EnhancementOutcome_WireValues_AreStable()
    {
        Assert.Equal(0, (byte)EnhancementOutcome.None);
        Assert.Equal(1, (byte)EnhancementOutcome.Success);
        Assert.Equal(2, (byte)EnhancementOutcome.NoChange);
        Assert.Equal(3, (byte)EnhancementOutcome.Downgrade);
        Assert.Equal(4, (byte)EnhancementOutcome.Destroyed);
        Assert.Equal((ushort)0x4031, ItemOpCode.EnhancementResult);
        Assert.Equal((ushort)0x4032, ItemOpCode.EnhancementAnnouncement);
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Carbon.Contracts.Entities.C2S;
using Carbon.Contracts.Entities.S2C;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Carbon.Contracts.Maps.S2C;
using Xunit;

namespace Carbon.Contracts.Tests;

/// <summary>
/// ADR-038 batch 2: the packets that lost a never-read field still round-trip every field they keep across the
/// <c>[SerializeOrder]</c> gap, and their payload is exactly the fields they keep (fixed-width layout), so a field
/// silently re-added, or a gap the serializer mishandles, turns these red.
/// </summary>
public sealed class DeadFieldRemovalContractTests
{
    [Fact]
    public void EntityDespawn_RoundTrip_KeepsNetworkIdOnly()
    {
        using EntityDespawn packet = new() { NetworkId = 0xABCD_0123_4567UL };
        using EntityDespawn copy = EntityDespawn.Deserialize(packet.Serialize());

        Assert.Equal(0xABCD_0123_4567UL, copy.NetworkId);
        Assert.Equal(sizeof(ulong), PayloadLength(packet.Serialize()));
    }

    [Fact]
    public void SkillCastBroadcast_RoundTrip_KeepsCasterSlotAndAim()
    {
        using SkillCastBroadcast packet = new() { CasterNetworkId = 55UL, AbilitySlot = 2, AimX = 40.5f, AimY = -60.25f };
        using SkillCastBroadcast copy = SkillCastBroadcast.Deserialize(packet.Serialize());

        Assert.Equal(55UL, copy.CasterNetworkId);
        Assert.Equal((byte)2, copy.AbilitySlot);
        Assert.Equal(40.5f, copy.AimX);
        Assert.Equal(-60.25f, copy.AimY);
        Assert.Equal(sizeof(ulong) + sizeof(byte) + (2 * sizeof(float)), PayloadLength(packet.Serialize()));
    }

    [Fact]
    public void MapPropChanged_RoundTrip_KeepsTileDurabilityAndDestroyed()
    {
        using MapPropChanged packet = new() { TileX = -12, TileY = 300, Durability = 7.5f, IsDestroyed = true };
        using MapPropChanged copy = MapPropChanged.Deserialize(packet.Serialize());

        Assert.Equal(-12, copy.TileX);
        Assert.Equal(300, copy.TileY);
        Assert.Equal(7.5f, copy.Durability);
        Assert.True(copy.IsDestroyed);
        Assert.Equal((2 * sizeof(int)) + sizeof(float) + sizeof(bool), PayloadLength(packet.Serialize()));
    }

    [Fact]
    public void MapStructureChanged_RoundTrip_KeepsTileTypeAndDoor()
    {
        using MapStructureChanged packet = new() { TileX = 9, TileY = -4, StructureType = 3, DoorState = 1 };
        using MapStructureChanged copy = MapStructureChanged.Deserialize(packet.Serialize());

        Assert.Equal(9, copy.TileX);
        Assert.Equal(-4, copy.TileY);
        Assert.Equal((byte)3, copy.StructureType);
        Assert.Equal((byte)1, copy.DoorState);
        Assert.Equal((2 * sizeof(int)) + (2 * sizeof(byte)), PayloadLength(packet.Serialize()));
    }

    [Fact]
    public void EntityAttackInput_RoundTrip_KeepsTargetAimAndSlot()
    {
        using EntityAttackInput packet = new() { TargetNetworkId = 901UL, AimX = 1.5f, AimY = -2.5f, AbilitySlot = 102 };
        using EntityAttackInput copy = EntityAttackInput.Deserialize(packet.Serialize());

        Assert.Equal(901UL, copy.TargetNetworkId);
        Assert.Equal(1.5f, copy.AimX);
        Assert.Equal(-2.5f, copy.AimY);
        Assert.Equal((byte)102, copy.AbilitySlot);
        Assert.Equal(sizeof(ulong) + (2 * sizeof(float)) + sizeof(byte), PayloadLength(packet.Serialize()));
    }

    [Fact]
    public void EnhancementResult_RoundTrip_KeepsStatusItemOutcomeLevelAndProtection()
    {
        using EnhancementResult packet = new()
        {
            Status = OpStatus.Ok,
            ItemId = 183,
            Outcome = EnhancementOutcome.Destroyed,
            NewLevel = 0,
            ProtectionConsumed = true,
        };
        using EnhancementResult copy = EnhancementResult.Deserialize(packet.Serialize());

        Assert.Equal(OpStatus.Ok, copy.Status);
        Assert.Equal((ushort)183, copy.ItemId);
        Assert.Equal(EnhancementOutcome.Destroyed, copy.Outcome);
        Assert.Equal((byte)0, copy.NewLevel);
        Assert.True(copy.ProtectionConsumed);
        Assert.Equal(sizeof(byte) + sizeof(ushort) + sizeof(byte) + sizeof(byte) + sizeof(bool), PayloadLength(packet.Serialize()));
    }

    [Fact]
    public void EnhancementAnnouncement_RoundTrip_KeepsNameItemOutcomeAndLevel()
    {
        using EnhancementAnnouncement packet = new() { PlayerName = "Smith", ItemId = 31, Outcome = EnhancementOutcome.Success, Level = 8 };
        using EnhancementAnnouncement copy = EnhancementAnnouncement.Deserialize(packet.Serialize());

        Assert.Equal("Smith", copy.PlayerName);
        Assert.Equal((ushort)31, copy.ItemId);
        Assert.Equal(EnhancementOutcome.Success, copy.Outcome);
        Assert.Equal((byte)8, copy.Level);
    }

    [Fact]
    public void PlayerOxygenSync_RoundTrip_KeepsOxygenOnly()
    {
        using PlayerOxygenSync packet = new() { OxygenMs = 12_500U, MaxOxygenMs = 30_000U };
        using PlayerOxygenSync copy = PlayerOxygenSync.Deserialize(packet.Serialize());

        Assert.Equal(12_500U, copy.OxygenMs);
        Assert.Equal(30_000U, copy.MaxOxygenMs);
        Assert.Equal(2 * sizeof(uint), PayloadLength(packet.Serialize()));
    }

    [Fact]
    public void ItemDurabilityAlert_RoundTrip_KeepsKindItemAndLevel()
    {
        using ItemDurabilityAlert packet = new() { Kind = DurabilityAlertKind.AboutToBreak, ItemId = 18, UpgradeLevel = 7 };
        using ItemDurabilityAlert copy = ItemDurabilityAlert.Deserialize(packet.Serialize());

        Assert.Equal(DurabilityAlertKind.AboutToBreak, copy.Kind);
        Assert.Equal((ushort)18, copy.ItemId);
        Assert.Equal((byte)7, copy.UpgradeLevel);
        Assert.Equal(sizeof(byte) + sizeof(ushort) + sizeof(byte), PayloadLength(packet.Serialize()));
    }

    [Fact]
    public void TraderStock_RoundTrip_KeepsStockPricesAndRepairFlag()
    {
        using TraderStock packet = new()
        {
            TraderNetworkId = 77UL,
            Kind = TraderStock.KindMerchant,
            Epoch = 12L,
            SecondsUntilRestock = 600,
            ItemIds = [4, 9],
            UnitPrices = [2, 15],
            Stocked = [20, 5],
            Remaining = [18, 5],
            SellItemIds = [4],
            SellPrices = [1],
            CanRepair = true,
        };
        using TraderStock copy = TraderStock.Deserialize(packet.Serialize());

        Assert.Equal(77UL, copy.TraderNetworkId);
        Assert.Equal(TraderStock.KindMerchant, copy.Kind);
        Assert.Equal(12L, copy.Epoch);
        Assert.Equal(600, copy.SecondsUntilRestock);
        Assert.Equal(packet.ItemIds, copy.ItemIds);
        Assert.Equal(packet.UnitPrices, copy.UnitPrices);
        Assert.Equal(packet.Stocked, copy.Stocked);
        Assert.Equal(packet.Remaining, copy.Remaining);
        Assert.Equal(packet.SellItemIds, copy.SellItemIds);
        Assert.Equal(packet.SellPrices, copy.SellPrices);
        Assert.True(copy.CanRepair);
    }

    [Fact]
    public void Version_IsAtLeastFiveSinceTheDeadFieldsLeftTheLayout() =>
        Assert.True(CarbonProtocol.Version >= 5, $"protocol {CarbonProtocol.Version} still uses the layouts with the dead fields");

    /// <summary>Bytes after the frame header, measured against a packet with no fields at all.</summary>
    private static int PayloadLength(byte[] frame)
    {
        using InventoryRequest empty = new();
        return frame.Length - empty.Serialize().Length;
    }
}

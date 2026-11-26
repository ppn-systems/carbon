// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Entities.C2S;
using Carbon.Contracts.Entities.S2C;
using Carbon.Contracts.Identity;
using Carbon.Contracts.Identity.C2S;
using Carbon.Contracts.Identity.S2C;
using Carbon.Contracts.Items;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Carbon.Contracts.Maps;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Xunit;

namespace Carbon.Contracts.Tests;

public sealed class ProtocolContractGuardTests
{
    private static readonly Assembly[] s_contractAssemblies =
    [
        typeof(SharedOpCode).Assembly,
        typeof(IdentityOpCode).Assembly,
        typeof(MapOpCode).Assembly,
        typeof(EntityOpCode).Assembly,
        typeof(ItemOpCode).Assembly,
    ];

    public static TheoryData<Type, ushort, ushort> ModuleBands => new()
    {
        { typeof(IdentityOpCode), 0x0100, 0x01FF },
        { typeof(MapOpCode), 0x1100, 0x11FF },
        { typeof(EntityOpCode), 0x1200, 0x12FF },
        { typeof(ItemOpCode), 0x4000, 0x40FF },
    };

    [Fact]
    public void AllContractAssemblies_OpCodes_AreGloballyUnique()
    {
        List<(Type Type, ushort OpCode)> all = [];
        for (int i = 0; i < s_contractAssemblies.Length; i++)
        {
            all.AddRange(PacketOpCodes(s_contractAssemblies[i]));
        }

        IGrouping<ushort, (Type Type, ushort OpCode)>? duplicate = all.GroupBy(static p => p.OpCode).FirstOrDefault(static g => g.Count() > 1);
        Assert.True(duplicate is null, $"Duplicate opcode 0x{duplicate?.Key:X4}: {string.Join(", ", duplicate?.Select(static p => p.Type.Name) ?? [])}");
    }

    [Theory]
    [MemberData(nameof(ModuleBands))]
    public void ContractModule_OpCodes_StayWithinBandAndBindEveryConstant(Type opCodeClass, ushort rangeStart, ushort rangeEnd)
    {
        ArgumentNullException.ThrowIfNull(opCodeClass);

        (Type Type, ushort OpCode)[] packets = [.. PacketOpCodes(opCodeClass.Assembly)];
        Assert.NotEmpty(packets);

        foreach ((Type type, ushort opCode) in packets)
        {
            Assert.True(
                opCode >= rangeStart && opCode <= rangeEnd,
                $"{type.Name} uses 0x{opCode:X4}, outside 0x{rangeStart:X4}-0x{rangeEnd:X4}.");
        }

        HashSet<ushort> used = [.. packets.Select(static p => p.OpCode)];
        IEnumerable<ushort> declared = opCodeClass
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static f => f.FieldType == typeof(ushort))
            .Select(static f => (ushort)f.GetValue(null)!);

        Assert.All(declared, op => Assert.Contains(op, used));
    }

    [Fact]
    public void AllPackets_SerializeOrders_AreUniqueWithinEachPacket()
    {
        for (int i = 0; i < s_contractAssemblies.Length; i++)
        {
            foreach (Type packetType in s_contractAssemblies[i].GetTypes().Where(IsPacketType))
            {
                List<int> orders = [];
                foreach (PropertyInfo prop in packetType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    SerializeOrderAttribute? attr = prop.GetCustomAttribute<SerializeOrderAttribute>();
                    if (attr is not null)
                    {
                        orders.Add(attr.Order);
                    }
                }

                int distinctCount = orders.Distinct().Count();
                Assert.True(
                    distinctCount == orders.Count,
                    $"Packet {packetType.Name} has duplicate [SerializeOrder] values.");
            }
        }
    }

    [Fact]
    public void NewContracts_RoundTrip_PreservesPayloads()
    {
        using AuthLogin login = new() { Username = "hero_01", Password = "secret_password" };
        using AuthLogin loginCopy = AuthLogin.Deserialize(login.Serialize());
        Assert.Equal("hero_01", loginCopy.Username);
        Assert.Equal("secret_password", loginCopy.Password);
        Assert.Equal(CarbonProtocol.Version, loginCopy.ClientProtocolVersion);

        Guid cid = Guid.NewGuid();
        using CharacterSummary summary = new()
        {
            CharacterId = cid,
            Name = "Aria",
            Level = 7,
            ZoneTier = 2,
            PositionX = 123.5f,
            PositionY = -45.25f,
            AppearancePreset = 3,
        };
        using CharacterSummary summaryCopy = CharacterSummary.Deserialize(summary.Serialize());
        Assert.Equal(cid, summaryCopy.CharacterId);
        Assert.Equal("Aria", summaryCopy.Name);
        Assert.Equal((ushort)7, summaryCopy.Level);
        Assert.Equal((byte)2, summaryCopy.ZoneTier);
        Assert.Equal((byte)3, summaryCopy.AppearancePreset);

        using CharacterEnterWorld enter = new()
        {
            Status = Carbon.Contracts.Common.OpStatus.Ok,
            PlayerNetworkId = 55UL,
            WorldSeed = 1337UL,
            GeneratorVersion = 2,
            TileSize = 32f,
            Frequency = 0.0008f,
            PositionX = 10f,
            PositionY = 20f,
            ServerTick = 120UL,
            TickRate = 30,
            AppearancePreset = 2,
        };
        using CharacterEnterWorld enterCopy = CharacterEnterWorld.Deserialize(enter.Serialize());
        Assert.Equal((byte)2, enterCopy.AppearancePreset);
        Assert.Equal(55UL, enterCopy.PlayerNetworkId);

        using EntityDeltaBatch batch = new()
        {
            ServerTick = 900,
            LastProcessedClientTick = 42,
            SelfPositionX = 10f,
            SelfPositionY = 20f,
            NetworkIds = [1001UL, 1002UL],
            DirtyMasks = [2, 6],
            PositionsX = [11f, 22f],
            PositionsY = [33f, 44f],
            VelocitiesX = [1f, -1f],
            VelocitiesY = [0f, 2f],
            CurrentHealths = [100f, 80f],
        };
        using EntityDeltaBatch batchCopy = EntityDeltaBatch.Deserialize(batch.Serialize());
        Assert.Equal(900UL, batchCopy.ServerTick);
        Assert.Equal(batch.NetworkIds, batchCopy.NetworkIds);
        Assert.Equal(batch.CurrentHealths, batchCopy.CurrentHealths);

        using InventorySlotItem slot = new()
        {
            SlotIndex = 3,
            ItemId = 404,
            Count = 12,
            Durability = 87.5f,
            UpgradeLevel = 5,
        };
        using InventorySlotItem slotCopy = InventorySlotItem.Deserialize(slot.Serialize());
        Assert.Equal((ushort)3, slotCopy.SlotIndex);
        Assert.Equal((ushort)404, slotCopy.ItemId);
        Assert.Equal(12, slotCopy.Count);
        Assert.Equal(87.5f, slotCopy.Durability);
        Assert.Equal((byte)5, slotCopy.UpgradeLevel);

        using PlayerStatsSync stats = new()
        {
            Level = 4,
            Experience = 210,
            XpToNextLevel = 325,
            CurrentHealth = 125f,
            MaxHealth = 150f,
            CurrentMana = 92f,
            MaxMana = 120f,
            CurrentStamina = 85f,
            MaxStamina = 115f,
            Defense = 18f,
            Skill1Cooldown = 1.5f,
            Skill2Cooldown = 0f,
            Skill3Cooldown = 4.2f,
            LeveledUp = true,
        };
        using PlayerStatsSync statsCopy = PlayerStatsSync.Deserialize(stats.Serialize());
        Assert.Equal((ushort)4, statsCopy.Level);
        Assert.Equal(210U, statsCopy.Experience);
        Assert.Equal(18f, statsCopy.Defense);
        Assert.True(statsCopy.LeveledUp);

        using EntityEquipmentChanged eq = new()
        {
            NetworkId = 77UL,
            EquippedHeadId = 17,
            EquippedChestId = 18,
            EquippedLegsId = 19,
            EquippedFeetId = 20,
            EquippedWeaponId = 3,
            WeaponUpgradeLevel = 6,
            Level = 5,
        };
        using EntityEquipmentChanged eqCopy = EntityEquipmentChanged.Deserialize(eq.Serialize());
        Assert.Equal(77UL, eqCopy.NetworkId);
        Assert.Equal((ushort)18, eqCopy.EquippedChestId);
        Assert.Equal((byte)6, eqCopy.WeaponUpgradeLevel);

        using MoveInventorySlot move = new() { FromSlot = 9, ToSlot = 37, SequenceId = 12 };
        using MoveInventorySlot moveCopy = MoveInventorySlot.Deserialize(move.Serialize());
        Assert.Equal((ushort)37, moveCopy.ToSlot);

        using UpgradeInventoryItem upg = new() { SlotIndex = 2, SequenceId = 3 };
        using UpgradeInventoryItem upgCopy = UpgradeInventoryItem.Deserialize(upg.Serialize());
        Assert.Equal((ushort)2, upgCopy.SlotIndex);

        using DropInventoryItem drop = new() { SlotIndex = 5, Quantity = 2, SequenceId = 9 };
        using DropInventoryItem dropCopy = DropInventoryItem.Deserialize(drop.Serialize());
        Assert.Equal(2, dropCopy.Quantity);

        using EntityPickupLoot pickup = new() { LootNetworkId = 9001UL };
        using EntityPickupLoot pickupCopy = EntityPickupLoot.Deserialize(pickup.Serialize());
        Assert.Equal(9001UL, pickupCopy.LootNetworkId);

        using SkillCastBroadcast cast = new()
        {
            CasterNetworkId = 55UL,
            AbilitySlot = 2,
            OriginX = 10f,
            OriginY = 20f,
            AimX = 40f,
            AimY = 60f,
        };
        using SkillCastBroadcast castCopy = SkillCastBroadcast.Deserialize(cast.Serialize());
        Assert.Equal((byte)2, castCopy.AbilitySlot);

        byte packed = CharacterAppearance.Pack(2, 3, 1, 2);
        CharacterAppearance app = new(packed);
        Assert.Equal((byte)2, app.HairStyle);
        Assert.Equal((byte)3, app.HairColor);
        Assert.Equal((byte)1, app.FaceStyle);
        Assert.Equal((byte)2, app.SkinTone);
    }

    private static bool IsPacketType(Type t) =>
        t is { IsClass: true, IsAbstract: false } && typeof(IPacketStaticOpcode).IsAssignableFrom(t);

    private static IEnumerable<(Type Type, ushort OpCode)> PacketOpCodes(Assembly assembly) =>
        assembly.GetTypes()
            .Where(IsPacketType)
            .Select(static t => (t, (ushort)t.GetProperty("StaticOpCode", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!));
}

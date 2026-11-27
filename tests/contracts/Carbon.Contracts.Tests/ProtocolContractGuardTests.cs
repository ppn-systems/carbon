// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Carbon.Contracts.Common;
using Carbon.Contracts.Config;
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
        typeof(ConfigOpCode).Assembly,
        typeof(IdentityOpCode).Assembly,
        typeof(MapOpCode).Assembly,
        typeof(EntityOpCode).Assembly,
        typeof(ItemOpCode).Assembly,
    ];

    public static TheoryData<Type, ushort, ushort> ModuleBands => new()
    {
        { typeof(IdentityOpCode), 0x0100, 0x01FF },
        { typeof(ConfigOpCode), 0x0200, 0x02FF },
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

    [Fact]
    public void AllOpCodeClasses_DeclaredConstants_AreGloballyUnique()
    {
        // Discover every contracts module next to the test binaries (not a hand-maintained list), so a new module or a
        // constant reused across modules fails CI even before a packet binds it.
        string[] modules = Directory.GetFiles(AppContext.BaseDirectory, "Carbon.Contracts.*.dll");
        string contractsRoot = Path.Combine(FindRepoRoot(), "contracts");
        string[] projects = Directory.GetDirectories(contractsRoot, "Carbon.Contracts.*");
        Assert.True(
            projects.Length <= modules.Length,
            $"Every contracts project must be referenced by Carbon.Contracts.Tests ({projects.Length} projects, {modules.Length} assemblies).");

        List<(string Name, ushort OpCode)> constants = [];
        foreach (string module in modules)
        {
            Assembly assembly = Assembly.LoadFrom(module);
            foreach (Type type in assembly.GetTypes().Where(static t => t.IsClass && t.IsAbstract && t.IsSealed && t.Name.EndsWith("OpCode", StringComparison.Ordinal)))
            {
                constants.AddRange(type
                    .GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(static f => f.IsLiteral && f.FieldType == typeof(ushort))
                    .Select(f => ($"{type.Name}.{f.Name}", (ushort)f.GetValue(null)!)));
            }
        }

        Assert.NotEmpty(constants);
        string[] duplicates =
        [
            .. constants
                .GroupBy(static c => c.OpCode)
                .Where(static g => g.Count() > 1)
                .Select(static g => $"0x{g.Key:X4}: {string.Join(", ", g.Select(static c => c.Name))}"),
        ];
        Assert.True(duplicates.Length == 0, $"Opcode collision(s): {string.Join("; ", duplicates)}");
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
            ClassId = (byte)HeroArchetypeClass.Arcanist,
            Traits = CharacterTraits.StarterLoadoutGranted | CharacterTraits.ForgeUnlocked,
        };
        using CharacterSummary summaryCopy = CharacterSummary.Deserialize(summary.Serialize());
        Assert.Equal(cid, summaryCopy.CharacterId);
        Assert.Equal("Aria", summaryCopy.Name);
        Assert.Equal((ushort)7, summaryCopy.Level);
        Assert.Equal((byte)2, summaryCopy.ZoneTier);
        Assert.Equal((byte)3, summaryCopy.AppearancePreset);
        Assert.Equal((byte)HeroArchetypeClass.Arcanist, summaryCopy.ClassId);
        Assert.Equal(CharacterTraits.StarterLoadoutGranted | CharacterTraits.ForgeUnlocked, summaryCopy.Traits);

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
            ClassId = (byte)HeroArchetypeClass.Ranger,
            Traits = CharacterTraits.StarterLoadoutGranted,
            UnlockedSkillMask = 0b0101UL,
            ProgressionBlob = [1, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        };
        using CharacterEnterWorld enterCopy = CharacterEnterWorld.Deserialize(enter.Serialize());
        Assert.Equal((byte)2, enterCopy.AppearancePreset);
        Assert.Equal(55UL, enterCopy.PlayerNetworkId);
        Assert.Equal((byte)HeroArchetypeClass.Ranger, enterCopy.ClassId);
        Assert.Equal(CharacterTraits.StarterLoadoutGranted, enterCopy.Traits);
        Assert.Equal(0b0101UL, enterCopy.UnlockedSkillMask);
        Assert.Equal(enter.ProgressionBlob, enterCopy.ProgressionBlob);

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
            FreshnessSeconds = 612.25f,
        };
        using InventorySlotItem slotCopy = InventorySlotItem.Deserialize(slot.Serialize());
        Assert.Equal(612.25f, slotCopy.FreshnessSeconds);

        using InventorySlotChanged changed = new() { SlotIndex = 4, ItemId = 121, Count = 3, FreshnessSeconds = 90.5f };
        using InventorySlotChanged changedCopy = InventorySlotChanged.Deserialize(changed.Serialize());
        Assert.Equal((ushort)121, changedCopy.ItemId);
        Assert.Equal(90.5f, changedCopy.FreshnessSeconds);
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
            LeveledUp = true,
            Traits = CharacterTraits.StarterLoadoutGranted,
            UnlockedSkillMask = CharacterSkillMask.Whirlwind | CharacterSkillMask.VitalSurge,
            CooldownSkillIds = [1, 3],
            CooldownRemainingSeconds = [1.5f, 4.2f],
            Food = 42.5f,
            MaxFood = 100f,
            HungerState = 1,
            Malnutrition = 0.25f,
            Mood = 38f,
            FoodPoisoned = true,
            EatProgress = 0.5f,
            Rest = 61.5f,
            MaxRest = 100f,
            RestState = 1,
            RestMode = RestMode.SleepingComfortable,
        };
        using PlayerStatsSync statsCopy = PlayerStatsSync.Deserialize(stats.Serialize());
        Assert.Equal(42.5f, statsCopy.Food);
        Assert.Equal(100f, statsCopy.MaxFood);
        Assert.Equal((byte)1, statsCopy.HungerState);
        Assert.Equal(0.25f, statsCopy.Malnutrition);
        Assert.Equal(38f, statsCopy.Mood);
        Assert.True(statsCopy.FoodPoisoned);
        Assert.Equal(0.5f, statsCopy.EatProgress);
        Assert.Equal(61.5f, statsCopy.Rest);
        Assert.Equal(100f, statsCopy.MaxRest);
        Assert.Equal((byte)1, statsCopy.RestState);
        Assert.Equal(RestMode.SleepingComfortable, statsCopy.RestMode);

        using EntityRestRequest restRequest = new() { Rest = true };
        using EntityRestRequest restCopy = EntityRestRequest.Deserialize(restRequest.Serialize());
        Assert.True(restCopy.Rest);
        Assert.Equal(EntityOpCode.RestRequest, EntityRestRequest.StaticOpCode);
        Assert.Equal((ushort)4, statsCopy.Level);
        Assert.Equal(210U, statsCopy.Experience);
        Assert.Equal(18f, statsCopy.Defense);
        Assert.True(statsCopy.LeveledUp);
        Assert.Equal(CharacterTraits.StarterLoadoutGranted, statsCopy.Traits);
        Assert.Equal(CharacterSkillMask.Whirlwind | CharacterSkillMask.VitalSurge, statsCopy.UnlockedSkillMask);
        Assert.Equal([1, 3], statsCopy.CooldownSkillIds);
        Assert.Equal([1.5f, 4.2f], statsCopy.CooldownRemainingSeconds);

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
            MaxHealth = 215f,
        };
        using EntityEquipmentChanged eqCopy = EntityEquipmentChanged.Deserialize(eq.Serialize());
        Assert.Equal(77UL, eqCopy.NetworkId);
        Assert.Equal((ushort)18, eqCopy.EquippedChestId);
        Assert.Equal((byte)6, eqCopy.WeaponUpgradeLevel);
        Assert.Equal(215f, eqCopy.MaxHealth);

        using CharacterEnterWorld enterFull = new()
        {
            Status = OpStatus.Ok,
            PlayerNetworkId = 11UL,
            WorldSeed = 1337UL,
            SelectedHotbarSlot = 2,
            Level = 7,
            Experience = 340U,
            XpToNextLevel = 550U,
            CurrentHealth = 185f,
            MaxHealth = 210f,
            CurrentMana = 95f,
            MaxMana = 140f,
            CurrentStamina = 112f,
            MaxStamina = 136f,
            Defense = 24f,
        };
        using CharacterEnterWorld enterFullCopy = CharacterEnterWorld.Deserialize(enterFull.Serialize());
        Assert.Equal((byte)2, enterFullCopy.SelectedHotbarSlot);
        Assert.Equal((ushort)7, enterFullCopy.Level);
        Assert.Equal(24f, enterFullCopy.Defense);

        Assert.Equal((byte)102, AbilityInputEncoding.EncodeSkill(2));
        Assert.True(AbilityInputEncoding.TryDecodeSkill(102, out ushort decodedSkill));
        Assert.Equal((ushort)2, decodedSkill);
        Assert.False(AbilityInputEncoding.TryDecodeSkill(2, out _));
        Assert.True(AbilityInputEncoding.TryDecodePlayerProjectileCast(AbilityInputEncoding.EncodePlayerProjectileCast(6), out byte pProj));
        Assert.Equal((byte)6, pProj);
        Assert.True(AbilityInputEncoding.TryDecodeMonsterProjectileCast(AbilityInputEncoding.EncodeMonsterProjectileCast(5), out byte mProj));
        Assert.Equal((byte)5, mProj);
        Assert.Equal((byte)8, (byte)OpStatus.EnhancementFailed);

        using MoveInventorySlot move = new() { FromSlot = 9, ToSlot = 37, SequenceId = 12 };
        using MoveInventorySlot moveCopy = MoveInventorySlot.Deserialize(move.Serialize());
        Assert.Equal((ushort)37, moveCopy.ToSlot);

        using UpgradeInventoryItem upg = new() { SlotIndex = 2, SequenceId = 3 };
        using UpgradeInventoryItem upgCopy = UpgradeInventoryItem.Deserialize(upg.Serialize());
        Assert.Equal((ushort)2, upgCopy.SlotIndex);

        using CharacterCreate charCreate = new()
        {
            CharacterName = "Spellweaver",
            AppearancePreset = 7,
        };
        using CharacterCreate charCreateCopy = CharacterCreate.Deserialize(charCreate.Serialize());
        Assert.Equal("Spellweaver", charCreateCopy.CharacterName);
        Assert.Equal((byte)7, charCreateCopy.AppearancePreset);

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

        using EntityAttackPhase phase = new()
        {
            NetworkId = 77UL,
            ServerTick = 123_456UL,
            AttackKind = EntityAttackPhase.KindSpell,
            Phase = EntityAttackPhase.PhaseWindup,
            Shape = 1,
            Interrupted = true,
            TargetX = 12.5f,
            TargetY = -4f,
            DirectionX = 0.6f,
            DirectionY = 0.8f,
            AreaRadius = 42f,
            HalfAngleRadians = 0.5f,
            PhaseDurationTicks = 24,
            Progress = 0.25f,
        };
        using EntityAttackPhase phaseCopy = EntityAttackPhase.Deserialize(phase.Serialize());
        Assert.Equal(77UL, phaseCopy.NetworkId);
        Assert.Equal(123_456UL, phaseCopy.ServerTick);
        Assert.Equal(EntityAttackPhase.KindSpell, phaseCopy.AttackKind);
        Assert.Equal(EntityAttackPhase.PhaseWindup, phaseCopy.Phase);
        Assert.Equal((byte)1, phaseCopy.Shape);
        Assert.True(phaseCopy.Interrupted);
        Assert.Equal(12.5f, phaseCopy.TargetX);
        Assert.Equal(-4f, phaseCopy.TargetY);
        Assert.Equal(0.8f, phaseCopy.DirectionY);
        Assert.Equal(42f, phaseCopy.AreaRadius);
        Assert.Equal(0.5f, phaseCopy.HalfAngleRadians);
        Assert.Equal((ushort)24, phaseCopy.PhaseDurationTicks);
        Assert.Equal(0.25f, phaseCopy.Progress);

        byte packed = CharacterAppearance.Pack(2, 3, 1, 2);
        CharacterAppearance app = new(packed);
        Assert.Equal((byte)2, app.HairStyle);
        Assert.Equal((byte)3, app.HairColor);
        Assert.Equal((byte)1, app.FaceStyle);
        Assert.Equal((byte)2, app.SkinTone);

        using EntityAllocatePoint alloc = new()
        {
            Kind = PointAllocationKind.Attribute,
            TargetId = (ushort)PrimaryAttributeId.Vitality,
            Amount = 2,
            SequenceId = 11,
        };
        using EntityAllocatePoint allocCopy = EntityAllocatePoint.Deserialize(alloc.Serialize());
        Assert.Equal(PointAllocationKind.Attribute, allocCopy.Kind);
        Assert.Equal((ushort)PrimaryAttributeId.Vitality, allocCopy.TargetId);
        Assert.Equal((byte)2, allocCopy.Amount);
        Assert.Equal(11U, allocCopy.SequenceId);

        using PlayerStatsSync advStats = new()
        {
            Level = 5,
            Traits = CharacterTraits.StarterLoadoutGranted,
            UnlockedSkillMask = CharacterSkillMask.Whirlwind | CharacterSkillMask.ArcaneNova,
            UnspentStatPoints = 16,
            UnspentSkillPoints = 3,
            ProgressionBlob = [1, 3, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 2, 0, 1, 0, 2],
            CooldownSkillIds = [1],
            CooldownRemainingSeconds = [2.5f],
        };
        using PlayerStatsSync advStatsCopy = PlayerStatsSync.Deserialize(advStats.Serialize());
        Assert.Equal((ushort)16, advStatsCopy.UnspentStatPoints);
        Assert.Equal((ushort)3, advStatsCopy.UnspentSkillPoints);
        Assert.Equal(CharacterSkillMask.Whirlwind | CharacterSkillMask.ArcaneNova, advStatsCopy.UnlockedSkillMask);
        Assert.Equal(advStats.ProgressionBlob, advStatsCopy.ProgressionBlob);
    }

    private static bool IsPacketType(Type t) =>
        t is { IsClass: true, IsAbstract: false } && typeof(IPacketStaticOpcode).IsAssignableFrom(t);

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "contracts")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("repository root (contracts/) not found");
    }

    private static IEnumerable<(Type Type, ushort OpCode)> PacketOpCodes(Assembly assembly) =>
        assembly.GetTypes()
            .Where(IsPacketType)
            .Select(static t => (t, (ushort)t.GetProperty("StaticOpCode", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!));
}

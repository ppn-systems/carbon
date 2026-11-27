// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Entities;
using Carbon.Contracts.Entities.S2C;
using Xunit;

namespace Carbon.Contracts.Tests;

/// <summary>ADR-019: the loot members appended to <see cref="EntitySpawn"/> survive the wire.</summary>
public sealed class EntitySpawnLootContractTests
{
    [Fact]
    public void EntitySpawn_RoundTrip_PreservesCurrencyOwnerAndProtection()
    {
        using EntitySpawn spawn = new()
        {
            NetworkId = 5_001UL,
            Kind = EntityKind.LootDrop,
            Name = "Gold",
            LootCount = 37,
            WeaponUpgradeLevel = 0,
            LootCurrency = LootCurrency.Gold,
            LootOwnerNetworkId = 900UL,
            LootProtectionMs = 59_967U,
        };

        using EntitySpawn copy = EntitySpawn.Deserialize(spawn.Serialize());

        Assert.Equal(EntityKind.LootDrop, copy.Kind);
        Assert.Equal(37, copy.LootCount);
        Assert.Equal(LootCurrency.Gold, copy.LootCurrency);
        Assert.Equal(900UL, copy.LootOwnerNetworkId);
        Assert.Equal(59_967U, copy.LootProtectionMs);
    }
}

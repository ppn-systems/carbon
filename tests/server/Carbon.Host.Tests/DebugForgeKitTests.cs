// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using Carbon.Abstractions.Identity;
using Carbon.Application.Identity;
using Carbon.Contracts.Common;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Infrastructure.Security;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>The Debug-host forge review kit (<c>CARBON_DEBUG_FORGE_KIT</c>) used by the forge screenshot scenarios (ADR-020).</summary>
public sealed class DebugForgeKitTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateCharacterAsync_DebugForgeKit_GrantsAPlusEightSwordScrollsAndGoldOnlyWhenEnabled(bool enabled)
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-forgekit-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            using AuthService auth = new(new SqliteAccountStore(factory), new SqliteCharacterStore(factory), new Pbkdf2PasswordHasher())
            {
                DebugForgeKit = enabled,
            };
            AuthOutcome account = await auth.RegisterAsync("smith", "secret-pass", CarbonProtocol.Version).ConfigureAwait(true);

            CharacterCreateResult created = await auth.CreateCharacterAsync(account.AccountId, "Smith", 0, 0f, 0f).ConfigureAwait(true);

            created.Succeeded.Should().BeTrue();
            InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
            InventoryBinaryCodec.TryDeserializeInto(created.Character!.InventoryBlob, bag).Should().BeTrue();
            ItemRules rules = ItemRules.LoadShippedDefaults();
            ItemSlot held = bag.GetSlot(created.Character.SelectedHotbarSlot);
            if (enabled)
            {
                held.ItemId.Should().Be(rules.ItemCatalog.ResolveId("iron_sword"));
                held.UpgradeLevel.Should().Be(8);
                bag.CountItem(rules.ItemCatalog.ResolveId("blessed_forge_scroll")).Should().BeGreaterThan(0);
                created.Character.Gold.Should().BeGreaterThan(0);
            }
            else
            {
                held.UpgradeLevel.Should().Be(0, "normal characters start without enhanced gear");
                bag.CountItem(rules.ItemCatalog.ResolveId("blessed_forge_scroll")).Should().Be(0);
                created.Character.Gold.Should().Be(0);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}

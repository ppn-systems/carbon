// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Serialization;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Nalix.Abstractions.Primitives;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>Saves from before the wood tool tier keep their item ids: id 1 is still the Wood Axe.</summary>
public sealed class SavedToolCompatibilityTests
{
    [Fact]
    public async Task TryEnterWorld_SaveWithTheOldWoodAxe_LoadsItAsTheWoodAxeAtFullDurability()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-old-axe-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            SqliteCharacterStore characters = new(factory);
            Guid accountId = Guid.NewGuid();
            (await new SqliteAccountStore(factory).TryCreateAsync(new AccountRecord(accountId, "veteran", "VETERAN", Bytes32.Zero, [2], DateTimeOffset.UnixEpoch, false)).ConfigureAwait(true)).Should().BeTrue();

            // Before this tier the wood axe had 120 durability; saves keep the raw id and durability.
            InventoryContainer oldBag = new(PlayerInventoryLayout.TotalCapacity);
            oldBag.SetSlot(0, new ItemSlot(1U, 1, 0, 120f));
            oldBag.SetSlot(1, new ItemSlot(2U, 1, 0, 160f));
            CharacterRecord veteran = new(
                Guid.NewGuid(), accountId, "Veteran", 3, 0, 0f, 0f, 100f, 100f, 0, InventoryBinaryCodec.Serialize(oldBag), DateTimeOffset.UnixEpoch);
            (await characters.TryCreateAsync(veteran).ConfigureAwait(true)).Should().BeTrue();

            AuthoritativeWorld world = new(
                1337UL, 32f, 0.0008f, 30, new SqliteChunkDeltaRepository(factory), characters, new AuthoritativeWorldMultiplayerTests.RecordingOutboundSink(),
                entropy: TestGameplayEntropy.Create(1337UL));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);
            CharacterRecord? loaded = await characters.FindByIdAsync(veteran.Id).ConfigureAwait(true);
            world.TryEnterWorld(921UL, loaded!).Status.Should().Be(OpStatus.Ok);

            List<(ushort SlotIndex, ItemSlot Slot)> slots = [];
            world.GetInventorySnapshot(921UL, slots).Should().BeTrue();
            ItemSlot axe = slots.Single(s => s.SlotIndex == 0).Slot;
            world.ItemRules.ItemCatalog.TryGet(axe.ItemId, out ItemCatalogEntry? entry).Should().BeTrue("id 1 is never reused or renumbered");
            entry!.Code.Should().Be("wood_axe");
            entry.Material.Should().Be(ItemMaterial.Wood);
            axe.Durability.Should().Be(entry.MaxDurability, "the old 120 durability is capped at the wood axe's new max");
            slots.Single(s => s.SlotIndex == 1).Slot.ItemId.Should().Be(StarterGameCatalog.StonePickaxe, "id 2 is still the stone pickaxe");
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

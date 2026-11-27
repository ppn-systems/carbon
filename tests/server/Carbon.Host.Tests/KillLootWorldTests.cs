// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Economy;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using RecordingOutboundSink = Carbon.Host.Tests.AuthoritativeWorldMultiplayerTests.RecordingOutboundSink;

namespace Carbon.Host.Tests;

/// <summary>ADR-019 end to end: a kill's gold lies on the ground; picking it up credits, syncs and persists the wallet.</summary>
public sealed class KillLootWorldTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)] // the killer logs out and back in during the protection window
    public async Task MonsterKill_GoldPilePickedUpByTheKiller_CreditsSyncsAndPersistsTheBalance(bool reconnect)
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-kill-gold-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            SqliteAccountStore accounts = new(factory);
            SqliteCharacterStore characters = new(factory);
            RecordingOutboundSink sink = new();
            GameEconomy economy = KillLootPickupTests.AlwaysGoldEconomy;
            AuthoritativeWorld world = new(
                1337UL,
                32f,
                0.0008f,
                30,
                new SqliteChunkDeltaRepository(factory),
                characters,
                sink,
                economy: economy,
                entropy: TestGameplayEntropy.Create(1337UL));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(bag);
            CharacterRecord template = new(
                Guid.NewGuid(), Guid.NewGuid(), "Scout", 1, 0, 0f, 0f, 100f, 100f, 2, InventoryBinaryCodec.Serialize(bag), DateTimeOffset.UtcNow);

            // Probe wilderness chunks until a monster backed by a loot table enters AoI.
            EntitySpawnSnapshot? mob = null;
            for (int probeX = 1; probeX <= 12 && mob is null; probeX++)
            {
                ulong probeConn = 700UL + (ulong)probeX;
                world.TryEnterWorld(probeConn, probeConn, template with { Id = Guid.NewGuid(), PositionX = ((probeX * 32) + 16) * 32f, PositionY = 16f * 32f }).Status.Should().Be(OpStatus.Ok);
                world.StepFixedTick();
                mob = sink.Spawns
                    .Where(x => x.ConnectionId == probeConn && x.Snapshot.Kind == EntityKind.Monster && AuthoritativeWorld.ResolveLootTableId(x.Snapshot.ArchetypeId) is not null)
                    .Select(x => (EntitySpawnSnapshot?)x.Snapshot)
                    .FirstOrDefault();
            }

            mob.Should().NotBeNull("wilderness chunks should populate a slime/skeleton/orc/demon");

            Guid accountId = Guid.NewGuid();
            (await accounts.TryCreateAsync(new AccountRecord(accountId, "slayer", "SLAYER", [1, 2, 3], [4, 5, 6], DateTimeOffset.UtcNow, false)).ConfigureAwait(true)).Should().BeTrue();
            CharacterRecord slayer = template with { Id = Guid.NewGuid(), AccountId = accountId, Name = "Slayer", PositionX = mob!.Value.PositionX + 16f, PositionY = mob.Value.PositionY };
            (await characters.TryCreateAsync(slayer).ConfigureAwait(true)).Should().BeTrue();

            const ulong slayerConn = 800UL;
            EnterWorldResult entered = world.TryEnterWorld(slayerConn, slayerConn, slayer);
            entered.Status.Should().Be(OpStatus.Ok);
            int killSpawnIndex = -1;
            for (int tick = 0; tick < 900 && killSpawnIndex < 0; tick++)
            {
                world.EnqueueAttackInput(slayerConn, mob.Value.NetworkId, new Vector2(mob.Value.PositionX, mob.Value.PositionY), 2);
                int before = sink.Spawns.Count;
                world.StepFixedTick();
                if (sink.CombatHits.Any(h => h.DefenderNetworkId == mob.Value.NetworkId && h.IsKilled))
                {
                    killSpawnIndex = before;
                }
            }

            killSpawnIndex.Should().BeGreaterThanOrEqualTo(0, "the slayer should eventually kill the monster");
            world.StepFixedTick();

            EntitySpawnSnapshot goldPile = sink.Spawns
                .Skip(killSpawnIndex)
                .Where(x => x.ConnectionId == slayerConn && x.Snapshot.Kind == EntityKind.LootDrop && x.Snapshot.LootCurrency == LootCurrency.Gold)
                .Select(x => x.Snapshot)
                .First();
            goldPile.LootCount.Should().BePositive();
            goldPile.LootOwnerNetworkId.Should().Be(entered.PlayerNetworkId, "the killer owns the pile");
            goldPile.LootProtectionMs.Should().BeInRange(59_000U, 60_000U);
            world.TryGetWalletBalances(slayerConn, out long goldAfterKill, out _).Should().BeTrue();
            goldAfterKill.Should().Be(0L, "kill gold is no longer credited directly");
            sink.WalletSyncs.Should().NotContain(w => w.ConnectionId == slayerConn && w.Gold > 0);

            ulong pickupConn = slayerConn;
            if (reconnect)
            {
                world.LeaveWorld(slayerConn).Should().BeTrue();
                pickupConn = 801UL;
                int beforeReentry = sink.Spawns.Count;
                EnterWorldResult reentered = world.TryEnterWorld(pickupConn, pickupConn, slayer with { PositionX = goldPile.PositionX + 40f, PositionY = goldPile.PositionY });
                reentered.Status.Should().Be(OpStatus.Ok);
                reentered.PlayerNetworkId.Should().NotBe(entered.PlayerNetworkId, "a reconnect gets a new entity");
                world.StepFixedTick();
                sink.Spawns.Skip(beforeReentry)
                    .Should().Contain(
                        x => x.ConnectionId == pickupConn && x.Snapshot.NetworkId == goldPile.NetworkId && x.Snapshot.LootOwnerNetworkId == reentered.PlayerNetworkId,
                        "the pile is replicated as owned by the killer's new entity, so its client draws it as its own");
            }

            world.EnqueuePickupLoot(pickupConn, goldPile.NetworkId).Should().Be(OpStatus.Ok);
            long gold = 0L;
            for (int tick = 0; tick < 30 && gold == 0L; tick++)
            {
                world.StepFixedTick();
                world.TryGetWalletBalances(pickupConn, out gold, out _).Should().BeTrue();
            }

            gold.Should().Be(goldPile.LootCount, "picking the pile up credits exactly its amount, also after a reconnect within the window");
            sink.WalletSyncs.Should().Contain(w => w.ConnectionId == pickupConn && w.Gold == gold, "the client learns its new balance");

            await world.FlushDirtyPersistenceAsync().ConfigureAwait(true);
            CharacterRecord? reloaded = await characters.FindByIdAsync(slayer.Id).ConfigureAwait(true);
            reloaded!.Gold.Should().Be(gold, "the credited balance is saved with the character");
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

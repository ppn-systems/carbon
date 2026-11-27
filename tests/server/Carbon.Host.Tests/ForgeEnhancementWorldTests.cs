// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Economy;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Serialization;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// Forge enhancement through the authoritative world (ADR-024): the server rolls, applies the outcome to the inventory and
/// wallet together, persists both, tells the owner, and announces notable outcomes to everyone in the world.
/// </summary>
public sealed class ForgeEnhancementWorldTests
{
    private const ulong Smith = 501UL;
    private const ulong Watcher = 502UL;

    [Fact]
    public async Task TryEnhanceInventoryItem_BlessedAttemptsAtPlus7_PersistLevelGoldAndCostsAndAnnounceTheSuccess()
    {
        using WorldFixture f = await WorldFixture.CreateAsync().ConfigureAwait(true);
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        bag.SetSlot(0, new ItemSlot(StarterGameCatalog.IronSword, 1, Durability: 200f, UpgradeLevel: 7));
        f.AddMaterials(bag, 99, 9, "mana_crystal", "iron_ingot");
        uint blessed = f.Economy.Items.ResolveId("blessed_forge_scroll");
        bag.SetSlot(20, new ItemSlot(blessed, 20));
        CharacterRecord smith = await f.CreateCharacterAsync("Smith", bag, gold: 100_000).ConfigureAwait(true);
        CharacterRecord watcher = await f.CreateCharacterAsync("Watcher", new InventoryContainer(PlayerInventoryLayout.TotalCapacity), gold: 0).ConfigureAwait(true);
        f.World.TryEnterWorld(Smith, smith).Status.Should().Be(OpStatus.Ok);
        f.World.TryEnterWorld(Watcher, watcher).Status.Should().Be(OpStatus.Ok);

        ForgeEnhancementReceipt receipt = default;
        int attempts = 0;
        for (uint seed = 1U; seed < 200U && receipt.Outcome != EnhancementOutcome.Success; seed++)
        {
            WorldMutationOutcome outcome = f.World.TryEnhanceInventoryItem(Smith, 0, (ushort)blessed, seed);
            outcome.Status.Should().Be(OpStatus.Ok);
            receipt = ForgeEnhancementReceipt.Unpack(outcome.Revision);
            receipt.ProtectionConsumed.Should().BeTrue();
            receipt.Outcome.Should().BeOneOf([EnhancementOutcome.Success, EnhancementOutcome.NoChange], "a blessed scroll never loses a level or the item");
            attempts++;
        }

        receipt.Outcome.Should().Be(EnhancementOutcome.Success);
        receipt.PreviousLevel.Should().Be(7);
        receipt.NewLevel.Should().Be(8);
        receipt.ItemId.Should().Be((ushort)StarterGameCatalog.IronSword);

        f.World.StepFixedTick(); // the wallet sync and the announcement go out with the tick dispatch
        long feePerAttempt = f.Economy.ItemRules.Enhancement.GetGoldFee(f.Economy.ItemRules.Enhancement.Steps[7], ItemRarity.Uncommon);
        long expectedGold = 100_000 - (feePerAttempt * attempts);
        f.Sink.WalletSyncs.Should().Contain(w => w.ConnectionId == Smith && w.Gold == expectedGold, "the owner gets its new balance");
        f.Sink.Announcements.Should().ContainSingle("only the +8 success is notable (announce.successMinLevel = 7)");
        (ulong[] recipients, string name, ushort itemId, EnhancementOutcome announced, byte level) = f.Sink.Announcements[0];
        recipients.Should().BeEquivalentTo([Smith, Watcher], "the announcement goes to the whole world");
        name.Should().Be("Smith");
        itemId.Should().Be((ushort)StarterGameCatalog.IronSword);
        announced.Should().Be(EnhancementOutcome.Success);
        level.Should().Be(8);

        // Persistence round-trip: the stored character carries the new level, the spent costs and the paid fee.
        await f.World.FlushDirtyPersistenceAsync().ConfigureAwait(true);
        CharacterRecord? stored = await f.Characters.FindByIdAsync(smith.Id).ConfigureAwait(true);
        stored.Should().NotBeNull();
        stored!.Gold.Should().Be(expectedGold);
        InventoryContainer reloaded = new(PlayerInventoryLayout.TotalCapacity);
        InventoryBinaryCodec.TryDeserializeInto(stored.InventoryBlob, reloaded).Should().BeTrue();
        reloaded.GetSlot(0).ItemId.Should().Be(StarterGameCatalog.IronSword);
        ItemEnhancementPolicy.GetUpgradeLevel(reloaded.GetSlot(0)).Should().Be(8);
        reloaded.CountItem(blessed).Should().Be(20 - attempts, "one scroll per attempt");
        reloaded.CountItem(f.Economy.Items.ResolveId("iron_ingot")).Should().Be(99 - (3 * attempts), "+7 costs 3 iron ingots per attempt");
    }

    [Fact]
    public async Task TryEnhanceInventoryItem_SameWorldSeedAndTicks_OutcomesFollowTheSecretNotTheSeed()
    {
        // ADR-026: both worlds share the public world seed (1337) and run the same attempts on the same ticks. Only the
        // server secret differs, and only the secret decides the outcomes.
        IReadOnlyList<EnhancementOutcome> first = await ForgeOutcomesAsync(secret: 11UL).ConfigureAwait(true);
        IReadOnlyList<EnhancementOutcome> replay = await ForgeOutcomesAsync(secret: 11UL).ConfigureAwait(true);
        IReadOnlyList<EnhancementOutcome> otherSecret = await ForgeOutcomesAsync(secret: 12UL).ConfigureAwait(true);

        replay.Should().Equal(first, "the same secret replays the same forge outcomes (deterministic tests and audits)");
        otherSecret.Should().NotEqual(first, "knowing the world seed and the tick is not enough to reproduce a forge outcome");
    }

    private static async Task<IReadOnlyList<EnhancementOutcome>> ForgeOutcomesAsync(ulong secret)
    {
        using WorldFixture f = await WorldFixture.CreateAsync(secret).ConfigureAwait(true);
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        const int swords = 12;
        for (ushort s = 0; s < swords; s++)
        {
            bag.SetSlot(s, new ItemSlot(StarterGameCatalog.IronSword, 1, Durability: 200f, UpgradeLevel: 9));
        }

        f.AddMaterials(bag, 198, swords, "iron_ingot");
        f.AddMaterials(bag, 297, swords + 2, "mana_crystal");
        f.AddMaterials(bag, 100, swords + 5, "rough_gem");
        CharacterRecord smith = await f.CreateCharacterAsync("Seer", bag, gold: 10_000_000).ConfigureAwait(true);
        f.World.TryEnterWorld(Smith, smith).Status.Should().Be(OpStatus.Ok);

        List<EnhancementOutcome> outcomes = [];
        for (ushort s = 0; s < swords; s++)
        {
            WorldMutationOutcome outcome = f.World.TryEnhanceInventoryItem(Smith, s, 0);
            outcome.Status.Should().Be(OpStatus.Ok);
            outcomes.Add(ForgeEnhancementReceipt.Unpack(outcome.Revision).Outcome);
        }

        return outcomes;
    }

    [Fact]
    public async Task TryEnhanceInventoryItem_PlusNineWithoutProtection_EventuallyShattersAnItemAndAnnouncesIt()
    {
        using WorldFixture f = await WorldFixture.CreateAsync().ConfigureAwait(true);
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        const int swords = 12;
        for (ushort s = 0; s < swords; s++)
        {
            bag.SetSlot(s, new ItemSlot(StarterGameCatalog.IronSword, 1, Durability: 200f, UpgradeLevel: 9));
        }

        f.AddMaterials(bag, 198, swords, "iron_ingot");
        f.AddMaterials(bag, 297, swords + 2, "mana_crystal");
        f.AddMaterials(bag, 100, swords + 5, "rough_gem");
        CharacterRecord smith = await f.CreateCharacterAsync("Breaker", bag, gold: 10_000_000).ConfigureAwait(true);
        f.World.TryEnterWorld(Smith, smith).Status.Should().Be(OpStatus.Ok);

        ForgeEnhancementReceipt destroyed = default;
        ushort destroyedSlot = 0;
        uint seed = 1U;
        for (ushort s = 0; s < swords && destroyed.Outcome != EnhancementOutcome.Destroyed; s++)
        {
            ForgeEnhancementReceipt receipt;
            do
            {
                WorldMutationOutcome outcome = f.World.TryEnhanceInventoryItem(Smith, s, 0, seed++);
                outcome.Status.Should().Be(OpStatus.Ok);
                receipt = ForgeEnhancementReceipt.Unpack(outcome.Revision);
            }
            while (receipt.Outcome == EnhancementOutcome.NoChange);

            destroyed = receipt;
            destroyedSlot = s;
        }

        f.World.StepFixedTick();
        destroyed.Outcome.Should().Be(EnhancementOutcome.Destroyed, "+9 destroys 15% of attempts; a dozen swords under a fixed test secret reach one");
        destroyed.PreviousLevel.Should().Be(9);
        List<(ushort SlotIndex, ItemSlot Slot)> snapshot = [];
        f.World.GetInventorySnapshot(Smith, snapshot).Should().BeTrue();
        snapshot.Should().NotContain(x => x.SlotIndex == destroyedSlot, "a destroyed item leaves its slot empty");
        f.Sink.SlotChanges.Should().Contain(c => c.ConnectionId == Smith && c.SlotIndex == destroyedSlot && c.ItemId == 0U);
        f.Sink.Announcements.Should().Contain(a => a.Outcome == EnhancementOutcome.Destroyed && a.Level == 9 && a.PlayerName == "Breaker");
    }

    [Fact]
    public async Task TryEnhanceInventoryItem_NotEnoughGold_IsAConflictAndChangesNothing()
    {
        using WorldFixture f = await WorldFixture.CreateAsync().ConfigureAwait(true);
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        bag.SetSlot(0, new ItemSlot(StarterGameCatalog.IronSword, 1, Durability: 200f, UpgradeLevel: 6));
        f.AddMaterials(bag, 10, 9, "iron_ingot", "mana_crystal");
        CharacterRecord smith = await f.CreateCharacterAsync("Pauper", bag, gold: 1).ConfigureAwait(true);
        f.World.TryEnterWorld(Smith, smith).Status.Should().Be(OpStatus.Ok);
        List<(ushort SlotIndex, ItemSlot Slot)> before = [];
        f.World.GetInventorySnapshot(Smith, before).Should().BeTrue();

        f.World.TryEnhanceInventoryItem(Smith, 0, 0, 1U).Should().Be(new WorldMutationOutcome(OpStatus.Conflict, 0U));

        List<(ushort SlotIndex, ItemSlot Slot)> after = [];
        f.World.GetInventorySnapshot(Smith, after).Should().BeTrue();
        after.Should().Equal(before);
        f.Sink.Announcements.Should().BeEmpty();
    }

    [Theory]
    [InlineData(EnhancementOutcome.Success, (ushort)183, (byte)9, (byte)10, true)]
    [InlineData(EnhancementOutcome.Destroyed, ushort.MaxValue, (byte)9, (byte)0, false)]
    [InlineData(EnhancementOutcome.NoChange, (ushort)1, (byte)0, (byte)0, false)]
    public void ForgeEnhancementReceipt_PackUnpack_RoundTrips(EnhancementOutcome outcome, ushort itemId, byte previous, byte next, bool protection)
    {
        ForgeEnhancementReceipt receipt = new(outcome, itemId, previous, next, protection);

        ForgeEnhancementReceipt.Unpack(receipt.Pack()).Should().Be(receipt);
    }

    private sealed class WorldFixture : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnectionFactory _factory;
        private readonly SqliteAccountStore _accounts;
        private readonly Guid _accountId = Guid.NewGuid();

        private WorldFixture(string dbPath, SqliteConnectionFactory factory, GameEconomy economy, ulong secret)
        {
            _dbPath = dbPath;
            _factory = factory;
            _accounts = new SqliteAccountStore(factory);
            this.Economy = economy;
            this.Characters = new SqliteCharacterStore(factory);
            this.World = new AuthoritativeWorld(
                1337UL,
                32f,
                0.0008f,
                30,
                new SqliteChunkDeltaRepository(factory),
                this.Characters,
                this.Sink,
                economy: economy,
                entropy: TestGameplayEntropy.Create(secret));
        }

        public GameEconomy Economy { get; }

        public SqliteCharacterStore Characters { get; }

        public AuthoritativeWorldMultiplayerTests.RecordingOutboundSink Sink { get; } = new();

        public AuthoritativeWorld World { get; }

        public static async Task<WorldFixture> CreateAsync(ulong secret = 1337UL)
        {
            string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-forge-test-{Guid.NewGuid():N}.db");
            SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            GameEconomy economy = GameEconomy.LoadFromDirectory(GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!);
            WorldFixture fixture = new(dbPath, factory, economy, secret);
            await fixture.World.InitializeFromStoreAsync().ConfigureAwait(true);
            (await fixture._accounts.TryCreateAsync(new AccountRecord(fixture._accountId, "forger", "FORGER", [1, 2, 3], [4, 5, 6], DateTimeOffset.UtcNow, false)).ConfigureAwait(true))
                .Should().BeTrue();
            return fixture;
        }

        public void AddMaterials(InventoryContainer bag, int quantity, int firstSlot, params string[] codes)
        {
            int slot = firstSlot;
            foreach (string code in codes)
            {
                uint id = this.Economy.Items.ResolveId(code);
                int remaining = quantity;
                while (remaining > 0)
                {
                    int stack = Math.Min(remaining, this.Economy.Items.GetMaxStack(id));
                    bag.SetSlot(slot++, new ItemSlot(id, stack));
                    remaining -= stack;
                }
            }
        }

        public async Task<CharacterRecord> CreateCharacterAsync(string name, InventoryContainer bag, long gold)
        {
            CharacterRecord record = new(
                Guid.NewGuid(),
                _accountId,
                name,
                1,
                0,
                this.World.DefaultSpawnWorldPosition.X,
                this.World.DefaultSpawnWorldPosition.Y,
                100f,
                100f,
                0,
                InventoryBinaryCodec.Serialize(bag),
                DateTimeOffset.UtcNow,
                Gold: gold);
            (await this.Characters.TryCreateAsync(record).ConfigureAwait(true)).Should().BeTrue();
            return record;
        }

        public void Dispose()
        {
            _factory.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
    }
}

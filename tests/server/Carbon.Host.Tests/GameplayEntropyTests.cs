// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Application.World.Entropy;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity.S2C;
using Carbon.Economy;
using Carbon.Economy.Trading;
using Carbon.Economy.Vendor;
using Carbon.Infrastructure.Configuration;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Primitives.Random;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-026: gameplay rolls use a server-only secret with per-epoch subkeys. The secret is deterministic when injected,
/// rotates with server time, survives restarts when persisted, and never reaches a client.
/// </summary>
public sealed class GameplayEntropyTests
{
    private const ushort TickRate = 30;

    [Fact]
    public void KeyAt_SameSecretAndSalt_IsDeterministic()
    {
        GameplayEntropy a = TestGameplayEntropy.Create(5UL, bootSalt: 9UL);
        GameplayEntropy b = TestGameplayEntropy.Create(5UL, bootSalt: 9UL);

        for (ulong tick = 0; tick < 3 * a.EpochTicks; tick += 997UL)
        {
            a.KeyAt(tick).Should().Be(b.KeyAt(tick), "the same secret and salt give the same keys (tests and replays)");
        }

        a.TraderStockKey.Should().Be(b.TraderStockKey);
    }

    [Fact]
    public void KeyAt_AcrossEpochs_RotatesTheRollStream()
    {
        GameplayEntropy entropy = TestGameplayEntropy.Create(5UL, tickRate: TickRate, epochSeconds: 600);
        entropy.EpochTicks.Should().Be(600UL * TickRate, "the default epoch is ten minutes of server time");

        GameplayKey epoch0 = entropy.KeyAt(0UL);
        entropy.KeyAt(entropy.EpochTicks - 1UL).Should().Be(epoch0, "a key holds for its whole epoch");
        GameplayKey epoch1 = entropy.KeyAt(entropy.EpochTicks);
        epoch1.Should().NotBe(epoch0, "the next epoch rolls on a new subkey");

        DeterministicRandom before = epoch0.CreateRandom(GameplayDomain.Loot, 42UL, 7UL);
        DeterministicRandom after = epoch1.CreateRandom(GameplayDomain.Loot, 42UL, 7UL);
        before.NextULong().Should().NotBe(after.NextULong(), "the same roll inputs give a different stream in another epoch");

        HashSet<GameplayKey> keys = [];
        for (ulong epoch = 0; epoch < 64; epoch++)
        {
            keys.Add(entropy.KeyAt(epoch * entropy.EpochTicks)).Should().BeTrue("every epoch has its own subkey");
        }
    }

    [Fact]
    public void BootSalt_ChangesRollKeysButNotTraderStock()
    {
        GameplayEntropy firstBoot = TestGameplayEntropy.Create(5UL, bootSalt: 1UL);
        GameplayEntropy secondBoot = TestGameplayEntropy.Create(5UL, bootSalt: 2UL);

        secondBoot.KeyAt(100UL).Should().NotBe(firstBoot.KeyAt(100UL), "ticks restart at zero, so a restart must not replay the last run's rolls");
        secondBoot.TraderStockKey.Should().Be(firstBoot.TraderStockKey, "trader stock depends on the persisted secret only");
        TestGameplayEntropy.Create(6UL).TraderStockKey.Should().NotBe(firstBoot.TraderStockKey);
    }

    [Fact]
    public void Constructor_ZeroSecret_IsRejected()
    {
        Action create = () => _ = new GameplayEntropy(default, 0UL, TickRate);

        create.Should().Throw<ArgumentException>("an all-zero key is an uninitialized secret");
    }

    [Fact]
    public void GenerateSecret_UsesFreshRandomness()
    {
        GameplayKey a = GameplayEntropy.GenerateSecret();
        GameplayKey b = GameplayEntropy.GenerateSecret();

        a.IsZero.Should().BeFalse();
        a.Should().NotBe(b, "each world gets its own 128-bit CSPRNG secret");
    }

    [Fact]
    public void ToString_IsRedacted()
    {
        GameplayEntropy entropy = TestGameplayEntropy.Create(0x1122334455667788UL);

        entropy.ToString().Should().Contain("redacted");
        entropy.ToString().ToUpperInvariant().Should().NotContain("1122334455667788");
        entropy.KeyAt(0UL).ToString().Should().Be("GameplayKey(redacted)");
    }

    [Fact]
    public async Task LoadAsync_NoInjectedSecret_PersistsTheFirstSecretAndReusesIt()
    {
        InMemoryGameplaySecretStore store = new();

        GameplayEntropy firstBoot = await GameplayEntropy.LoadAsync(store, null, null, TickRate).ConfigureAwait(true);
        GameplayEntropy secondBoot = await GameplayEntropy.LoadAsync(store, null, null, TickRate).ConfigureAwait(true);

        secondBoot.TraderStockKey.Should().Be(firstBoot.TraderStockKey, "the second boot loads the secret the first boot stored");
        secondBoot.KeyAt(0UL).Should().NotBe(firstBoot.KeyAt(0UL), "each boot draws a fresh salt for transient rolls");
    }

    [Fact]
    public async Task LoadAsync_InjectedSecret_NeverTouchesTheStore()
    {
        GameplayKey injected = TestGameplayEntropy.Key(77UL);

        GameplayEntropy entropy = await GameplayEntropy.LoadAsync(new ThrowingSecretStore(), injected, 3UL, TickRate).ConfigureAwait(true);

        entropy.KeyAt(10UL).Should().Be(TestGameplayEntropy.Create(77UL, bootSalt: 3UL).KeyAt(10UL));
    }

    [Fact]
    public async Task SqliteGameplaySecretStore_GetOrCreate_KeepsTheFirstSecretAcrossConnectionsAndMigrations()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-gameplay-secret-{Guid.NewGuid():N}.db");
        try
        {
            byte[] first = new byte[GameplayKey.SizeInBytes];
            byte[] second = new byte[GameplayKey.SizeInBytes];
            TestGameplayEntropy.Key(1UL).WriteBytes(first);
            TestGameplayEntropy.Key(2UL).WriteBytes(second);

            using (SqliteConnectionFactory factory = new($"Data Source={dbPath}"))
            {
                SqliteSchemaMigrator migrator = new(factory);
                await migrator.MigrateAsync().ConfigureAwait(true);
                await migrator.MigrateAsync().ConfigureAwait(true);
                (await new SqliteGameplaySecretStore(factory).GetOrCreateAsync(first).ConfigureAwait(true)).Should().Equal(first);
                (await new SqliteGameplaySecretStore(factory).GetOrCreateAsync(second).ConfigureAwait(true))
                    .Should().Equal(first, "a world's secret is written once and never replaced");
            }

            SqliteConnection.ClearAllPools();
            using SqliteConnectionFactory reopened = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(reopened).MigrateAsync().ConfigureAwait(true);
            (await new SqliteGameplaySecretStore(reopened).GetOrCreateAsync(second).ConfigureAwait(true))
                .Should().Equal(first, "the secret survives a restart");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task TraderStock_PersistedSecret_IsStableAcrossARestartWithANewBootSalt()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-gameplay-trader-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            SqliteGameplaySecretStore store = new(factory);
            TraderCatalog catalog = LoadEconomy().Traders!;
            PriceBook prices = LoadEconomy().Prices;
            TraderIdentity trader = new(4242UL, "merchant", 0);
            const long Now = 1_800_000_000L;

            GameplayEntropy beforeRestart = await GameplayEntropy.LoadAsync(store, null, null, TickRate).ConfigureAwait(true);
            GameplayEntropy afterRestart = await GameplayEntropy.LoadAsync(store, null, null, TickRate).ConfigureAwait(true);
            GameplayEntropy otherWorld = TestGameplayEntropy.Create(999UL);

            TraderOffer[] before = Offers(new TraderBook(beforeRestart.TraderStockKey), catalog, prices, trader, Now);
            Offers(new TraderBook(afterRestart.TraderStockKey), catalog, prices, trader, Now)
                .Should().Equal(before, "trader stock per restock epoch must not reshuffle when the server restarts");
            Offers(new TraderBook(otherWorld.TraderStockKey), catalog, prices, trader, Now)
                .Should().NotEqual(before, "stock follows the secret, so knowing the world seed does not reveal it");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void ReadGameplayEntropy_ValidVariables_ParsesSecretSaltAndEpoch()
    {
        Dictionary<string, string> env = new()
        {
            ["CARBON_GAMEPLAY_SECRET"] = "000102030405060708090A0B0C0D0E0F",
            ["CARBON_GAMEPLAY_BOOT_SALT"] = "BEEF",
            ["CARBON_GAMEPLAY_EPOCH_SECONDS"] = "120",
        };

        (GameplayKey? secret, ulong? salt, int epochSeconds) = HostRuntimeOptions.ReadGameplayEntropy(env.GetValueOrDefault);

        byte[] expected = new byte[GameplayKey.SizeInBytes];
        for (int i = 0; i < expected.Length; i++)
        {
            expected[i] = (byte)i;
        }

        secret.Should().Be(GameplayKey.FromBytes(expected));
        salt.Should().Be(0xBEEFUL);
        epochSeconds.Should().Be(120);
    }

    [Fact]
    public void ReadGameplayEntropy_NothingSet_UsesPersistedSecretAndDefaultEpoch()
    {
        (GameplayKey? secret, ulong? salt, int epochSeconds) = HostRuntimeOptions.ReadGameplayEntropy(_ => null);

        secret.Should().BeNull("the secret then comes from the database");
        salt.Should().BeNull("the salt is then random per boot");
        epochSeconds.Should().Be(HostRuntimeOptions.DefaultGameplayEpochSeconds);
    }

    [Theory]
    [InlineData("00112233")]
    [InlineData("000000000000000000000000000000000")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("ZZ0102030405060708090A0B0C0D0E0F")]
    public void ReadGameplayEntropy_MalformedSecret_FailsBootWithoutEchoingTheValue(string raw)
    {
        Action read = () => HostRuntimeOptions.ReadGameplayEntropy(name => name == "CARBON_GAMEPLAY_SECRET" ? raw : null);

        read.Should().Throw<InvalidOperationException>().Which.Message.Should().NotContain(raw);
    }

    [Fact]
    public async Task S2CPackets_CarryingTheWorldSeed_NeverContainTheGameplaySecret()
    {
        const ulong SecretLabel = 0x5EC2E7UL;
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-gameplay-leak-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            GameplayEntropy entropy = TestGameplayEntropy.Create(SecretLabel, bootSalt: 0xB007UL);
            AuthoritativeWorld world = new(
                1337UL,
                32f,
                0.0008f,
                TickRate,
                new SqliteChunkDeltaRepository(factory),
                new SqliteCharacterStore(factory),
                new AuthoritativeWorldMultiplayerTests.RecordingOutboundSink(),
                entropy: entropy);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);
            CharacterRecord hero = new(Guid.NewGuid(), Guid.NewGuid(), "Leaky", 1, default, world.DefaultSpawnWorldPosition.X, world.DefaultSpawnWorldPosition.Y, 100f, 100f, 0, [], DateTimeOffset.UtcNow);
            EnterWorldResult entered = world.TryEnterWorld(7UL, hero);
            entered.Status.Should().Be(OpStatus.Ok);
            world.StepFixedTick();

            // Built exactly as IdentityHandlers.SendEnterWorldAsync builds it: the only packet that carries the world seed.
            using CharacterEnterWorld enter = new()
            {
                Status = entered.Status,
                PlayerNetworkId = entered.PlayerNetworkId,
                WorldSeed = entered.WorldSeed,
                TileSize = entered.TileSize,
                Frequency = entered.Frequency,
                ServerTick = entered.ServerTick,
                TickRate = entered.TickRate,
            };

            List<byte[]> secrets = SecretMaterial(TestGameplayEntropy.Key(SecretLabel), entropy, world.CurrentTick);
            foreach (byte[] wire in new[] { enter.Serialize() })
            {
                foreach (byte[] secret in secrets)
                {
                    wire.AsSpan().IndexOf(secret).Should().Be(-1, "no S2C packet may carry the gameplay secret or a subkey");
                }
            }

            entered.WorldSeed.Should().Be(1337UL, "clients still receive the public terrain seed");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    /// <summary>Every 8-byte half of the secret, the trader key and the live roll key.</summary>
    private static List<byte[]> SecretMaterial(GameplayKey secret, GameplayEntropy entropy, ulong tick)
    {
        List<byte[]> halves = [];
        foreach (GameplayKey key in new[] { secret, entropy.TraderStockKey, entropy.KeyAt(tick) })
        {
            byte[] bytes = new byte[GameplayKey.SizeInBytes];
            key.WriteBytes(bytes);
            halves.Add(bytes[..8]);
            halves.Add(bytes[8..]);
        }

        return halves;
    }

    private static GameEconomy LoadEconomy() => GameEconomy.LoadFromDirectory(GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!);

    private static TraderOffer[] Offers(TraderBook book, TraderCatalog catalog, PriceBook prices, TraderIdentity trader, long now)
    {
        TraderOffer[] offers = new TraderOffer[TraderCatalog.MaxSlots];
        int count = book.GetOffers(catalog, prices, trader, now, 10, offers, out _, out _);
        count.Should().BeGreaterThan(0);
        return offers[..count];
    }

    private sealed class ThrowingSecretStore : IGameplaySecretStore
    {
        public Task<byte[]> GetOrCreateAsync(ReadOnlyMemory<byte> candidate, CancellationToken ct = default) =>
            throw new InvalidOperationException("an injected secret must not read or write the store");
    }
}

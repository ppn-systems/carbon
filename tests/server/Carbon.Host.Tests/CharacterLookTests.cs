// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Carbon.Abstractions.Identity;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.Identity;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;
using Carbon.Economy;
using Carbon.Infrastructure.Persistence;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Infrastructure.Security;
using Carbon.Items.Races;
using Carbon.Survival.Config;
using Carbon.Survival.Food;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// Structured character looks on the server (ADR-042): migration 0022 from the packed byte, validation on create,
/// storage, replication, and a race that exists only in config.
/// </summary>
public sealed class CharacterLookTests : IDisposable
{
    /// <summary>A race no code knows: only this config defines it. Two bodies, one palette and one part option, +30 HP.</summary>
    private const string TestRaceJson = """
        {"schemaVersion":1,"races":[
          {"id":1,"code":"human","nameKey":"race.human",
           "bodies":[{"id":0,"code":"male","nameKey":"race.body.male","rig":"characters/base_hero.cvg.json"}],
           "options":[{"code":"skin_tone","nameKey":"appearance.skin","role":"skin","choices":[{"nameKey":"appearance.skin.fair","colors":["#F5C6A5","#D49B78"]}]}]},
          {"id":4242,"code":"test_kin","nameKey":"race.test_kin",
           "bodies":[{"id":0,"code":"broad","nameKey":"race.body.broad","rig":"characters/base_hero.cvg.json"},
                     {"id":5,"code":"slim","nameKey":"race.body.slim","rig":"characters/test_kin_slim.cvg.json"}],
           "options":[{"code":"scales","nameKey":"appearance.scales","role":"skin","choices":[
                        {"nameKey":"appearance.scales.jade","colors":["#2F855A","#22543D"]},
                        {"nameKey":"appearance.scales.ember","colors":["#C05621","#7B341E"]}]},
                      {"code":"crest","nameKey":"appearance.crest","role":"hair","choices":[
                        {"nameKey":"appearance.crest.none","part":"crest_none"},
                        {"nameKey":"appearance.crest.fan","part":"crest_fan"},
                        {"nameKey":"appearance.crest.spike","part":"crest_spike"}]}],
           "statModifiers":{"maxHealth":30}}]}
        """;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"carbon-look-{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteCharacterStore _characters;

    public CharacterLookTests()
    {
        _factory = new SqliteConnectionFactory($"Data Source={_dbPath}");
        new SqliteSchemaMigrator(_factory).MigrateAsync().GetAwaiter().GetResult();
        _characters = new SqliteCharacterStore(_factory);
    }

    [Fact]
    public void ToLook_EveryLegacyPreset_MapsTheOldUnpackedTraitsOntoHumanMaleOptions()
    {
        for (int hair = 0; hair < LegacyCharacterAppearance.TraitVariantCount; hair++)
        {
            for (int color = 0; color < 4; color++)
            {
                for (int face = 0; face < 4; face++)
                {
                    for (int skin = 0; skin < 4; skin++)
                    {
                        LegacyCharacterAppearance old = new(LegacyCharacterAppearance.Pack((byte)hair, (byte)color, (byte)face, (byte)skin));

                        CharacterLook look = LegacyAppearanceMigration.ToLook(old.Preset);

                        look.Should().Be(
                            new CharacterLook(1, 0, [old.HairStyle, old.HairColor, old.FaceStyle, old.SkinTone]),
                            $"preset {old.Preset} is hair {hair}, colour {color}, eyes {face}, skin {skin}");
                    }
                }
            }
        }
    }

    [Fact]
    public void ToLook_EveryLegacyPreset_IsAValidShippedLook()
    {
        RaceCatalog shipped = RaceCatalog.LoadShippedDefaults();
        for (int preset = 0; preset <= byte.MaxValue; preset++)
        {
            shipped.Check(LegacyAppearanceMigration.ToLook((byte)preset)).Should().Be(CharacterLookStatus.Valid, $"legacy preset {preset} must survive migration");
        }
    }

    [Fact]
    public async Task Migrate_LegacyRowsWithPackedPresets_StoresTheirLooksAndDropsTheOldColumn()
    {
        // Rewind this database to before 0022: the packed column is back and holds every byte value.
        await this.ExecuteAsync(
            "ALTER TABLE characters ADD COLUMN appearance_preset INTEGER NOT NULL DEFAULT 0;",
            $"DELETE FROM schema_migrations WHERE id = '{LegacyAppearanceMigration.MigrationId}';").ConfigureAwait(true);
        Guid account = await this.AccountAsync("legacy").ConfigureAwait(true);
        Dictionary<Guid, byte> presets = [];
        for (int preset = 0; preset <= byte.MaxValue; preset++)
        {
            CharacterRecord legacy = Character(account, $"Legacy{preset:D3}", default);
            (await _characters.TryCreateAsync(legacy).ConfigureAwait(true)).Should().BeTrue();
            presets[legacy.Id] = (byte)preset;
            await this.ExecuteAsync($"UPDATE characters SET appearance = X'', appearance_preset = {preset} WHERE id = '{legacy.Id:D}';").ConfigureAwait(true);
        }

        await new SqliteSchemaMigrator(_factory).MigrateAsync().ConfigureAwait(true);

        IReadOnlyList<CharacterRecord> migrated = await _characters.ListByAccountAsync(account).ConfigureAwait(true);
        migrated.Should().HaveCount(256);
        foreach (CharacterRecord c in migrated)
        {
            LegacyCharacterAppearance old = new(presets[c.Id]);
            c.Look.Should().Be(new CharacterLook(1, 0, [old.HairStyle, old.HairColor, old.FaceStyle, old.SkinTone]), $"{c.Name} keeps its traits");
        }

        (await this.ColumnsAsync().ConfigureAwait(true)).Should().Contain("appearance").And.NotContain("appearance_preset", "no dead column is left behind");
    }

    [Fact]
    public async Task Migrate_RunTwice_LeavesStoredLooksAlone()
    {
        CharacterLook look = new(1, 0, [3, 2, 1, 0]);
        CharacterRecord hero = Character(await this.AccountAsync("twice").ConfigureAwait(true), "Twice", look);
        (await _characters.TryCreateAsync(hero).ConfigureAwait(true)).Should().BeTrue();

        await new SqliteSchemaMigrator(_factory).MigrateAsync().ConfigureAwait(true);

        (await _characters.FindByIdAsync(hero.Id).ConfigureAwait(true))!.Look.Should().Be(look);
    }

    [Fact]
    public async Task SaveBatch_ProgressedCharacter_KeepsTheLookItWasCreatedWith()
    {
        CharacterLook look = new(1, 0, [1, 3, 0, 2]);
        CharacterRecord hero = Character(await this.AccountAsync("keeper").ConfigureAwait(true), "Keeper", look);
        (await _characters.TryCreateAsync(hero).ConfigureAwait(true)).Should().BeTrue();

        await _characters.SaveBatchAsync([hero with { Level = 9, Look = new CharacterLook(1, 0, [0, 0, 0, 0]) }]).ConfigureAwait(true);

        CharacterRecord stored = (await _characters.FindByIdAsync(hero.Id).ConfigureAwait(true))!;
        stored.Level.Should().Be(9);
        stored.Look.Should().Be(look, "a look is chosen once, at creation; saves never rewrite it");
    }

    [Theory]
    [InlineData((ushort)0, (byte)0, new byte[] { 0, 0, 0, 0 })]
    [InlineData((ushort)77, (byte)0, new byte[] { 0, 0, 0, 0 })]
    [InlineData((ushort)1, (byte)2, new byte[] { 0, 0, 0, 0 })]
    [InlineData((ushort)1, (byte)0, new byte[] { 0, 0, 0 })]
    [InlineData((ushort)1, (byte)0, new byte[] { 0, 0, 0, 4 })]
    public async Task CreateCharacterAsync_LookNotInRacesJson_IsRejectedAndNothingIsStored(ushort raceId, byte bodyType, byte[] options)
    {
        using AuthService auth = this.CreateAuth(RaceCatalog.LoadShippedDefaults());
        Guid account = await RegisterAsync(auth, "looker").ConfigureAwait(true);

        CharacterCreateResult result = await auth.CreateCharacterAsync(account, "Badlook", new CharacterLook(raceId, bodyType, options), 0f, 0f).ConfigureAwait(true);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("invalid_look");
        (await _characters.ListByAccountAsync(account).ConfigureAwait(true)).Should().BeEmpty();
    }

    [Fact]
    public async Task ConfigOnlyRace_CreateStoreEnterAndReplicate_WorksWithoutCodeChanges()
    {
        RaceCatalog races = RaceCatalog.LoadFromJson(TestRaceJson);
        using AuthService auth = this.CreateAuth(races);
        Guid account = await RegisterAsync(auth, "kinfolk").ConfigureAwait(true);
        CharacterLook kin = new(4242, 5, [1, 2]);
        CharacterLook human = new(1, 0, [0]);

        CharacterCreateResult created = await auth.CreateCharacterAsync(account, "Kinling", kin, 0f, 0f).ConfigureAwait(true);
        CharacterCreateResult plain = await auth.CreateCharacterAsync(account, "Plainly", human, 0f, 0f).ConfigureAwait(true);

        created.Succeeded.Should().BeTrue("race 4242 is defined by config alone");
        plain.Succeeded.Should().BeTrue();
        created.Character!.MaxHealth.Should().Be(plain.Character!.MaxHealth + 30f, "the race's statModifiers apply from creation");
        CharacterRecord storedKin = (await _characters.FindByIdAsync(created.Character.Id).ConfigureAwait(true))!;
        storedKin.Look.Should().Be(kin, "the look round-trips through the database");

        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        AuthoritativeWorld world = await this.CreateWorldAsync(sink, races).ConfigureAwait(true);
        EnterWorldResult enteredKin = world.TryEnterWorld(41UL, storedKin);
        EnterWorldResult enteredHuman = world.TryEnterWorld(42UL, (await _characters.FindByIdAsync(plain.Character.Id).ConfigureAwait(true))!);
        world.StepFixedTick();

        enteredKin.Status.Should().Be(OpStatus.Ok);
        enteredKin.Look.Should().Be(kin, "CharacterEnterWorld carries the stored look");
        enteredKin.MaxHealth.Should().Be(enteredHuman.MaxHealth + 30f, "the world applies the race's stat changes too");
        EntitySpawnSnapshot seen = sink.Spawns.First(s => s.ConnectionId == 42UL && s.Snapshot.NetworkId == enteredKin.PlayerNetworkId).Snapshot;
        seen.Kind.Should().Be(EntityKind.Player);
        seen.Look.Should().Be(kin, "observers receive the look in EntitySpawn");
        seen.ArchetypeId.Should().Be(0);
    }

    [Fact]
    public async Task ApplyGameData_RaceModifierChanges_RecomputeConnectedPlayersStats()
    {
        RaceCatalog races = RaceCatalog.LoadFromJson(TestRaceJson);
        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        AuthoritativeWorld world = await this.CreateWorldAsync(sink, RaceCatalog.LoadShippedDefaults()).ConfigureAwait(true);
        CharacterRecord kin = Character(Guid.NewGuid(), "Reloaded", new CharacterLook(4242, 0, [0, 0]));
        float before = world.TryEnterWorld(51UL, kin).MaxHealth;

        ApplyRaces(world, races);
        world.StepFixedTick();

        sink.LatestStats[51UL].MaxHealth.Should().Be(before + 30f, "a hot reload of races.json re-reads the race's stat changes");
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

    private static CharacterRecord Character(Guid account, string name, CharacterLook look) =>
        new(Guid.NewGuid(), account, name, 1, look, 16f * 32f, 16f * 32f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);

    private static async Task<Guid> RegisterAsync(AuthService auth, string username)
    {
        AuthOutcome registered = await auth.RegisterAsync(username, "secret-pass", CarbonProtocol.Version).ConfigureAwait(true);
        registered.Status.Should().Be(AuthStatus.Ok);
        return registered.AccountId;
    }

    private static void ApplyRaces(AuthoritativeWorld world, RaceCatalog races)
    {
        string configDir = GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!;
        NeedsConfig needs = NeedsConfig.LoadFromJson(File.ReadAllText(Path.Combine(configDir, "needs.json")));
        FoodCatalog food = FoodCatalog.LoadFromJson(File.ReadAllText(Path.Combine(configDir, "food.json")), needs.FoodMax);
        world.ApplyGameData(world.Economy, needs, food, races: races);
    }

    private async Task<Guid> AccountAsync(string username)
    {
        using AuthService auth = this.CreateAuth(RaceCatalog.LoadShippedDefaults());
        return await RegisterAsync(auth, username).ConfigureAwait(true);
    }

    private AuthService CreateAuth(RaceCatalog races) =>
        new(new SqliteAccountStore(_factory), _characters, new Pbkdf2PasswordHasher(), races: () => races);

    private async Task<AuthoritativeWorld> CreateWorldAsync(AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink, RaceCatalog races)
    {
        AuthoritativeWorld world = new(
            1337UL, 32f, 0.0008f, 30, new SqliteChunkDeltaRepository(_factory), _characters, sink,
            entropy: TestGameplayEntropy.Create(1337UL));
        await world.InitializeFromStoreAsync().ConfigureAwait(true);
        ApplyRaces(world, races);
        return world;
    }

    private async Task ExecuteAsync(params string[] statements)
    {
        using SqliteConnection conn = await _factory.OpenAsync().ConfigureAwait(true);
        foreach (string sql in statements)
        {
#pragma warning disable CA2100 // Test-only constant SQL.
            using SqliteCommand cmd = new(sql, conn);
#pragma warning restore CA2100
            _ = await cmd.ExecuteNonQueryAsync().ConfigureAwait(true);
        }
    }

    private async Task<List<string>> ColumnsAsync()
    {
        List<string> columns = [];
        using SqliteConnection conn = await _factory.OpenAsync().ConfigureAwait(true);
        using SqliteCommand cmd = new("PRAGMA table_info(characters);", conn);
        using SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(true);
        while (await reader.ReadAsync().ConfigureAwait(true))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    /// <summary>Verbatim copy of the retired <c>Carbon.Contracts.Identity.CharacterAppearance</c> (protocol ≤ 7), for characterization.</summary>
    private readonly record struct LegacyCharacterAppearance(byte Preset)
    {
        public const byte TraitVariantCount = 4;

        public byte HairStyle => (byte)(this.Preset & 0x03);

        public byte HairColor => (byte)((this.Preset >> 2) & 0x03);

        public byte FaceStyle => (byte)((this.Preset >> 4) & 0x03);

        public byte SkinTone => (byte)((this.Preset >> 6) & 0x03);

        public static byte Pack(byte hairStyle, byte hairColor, byte faceStyle, byte skinTone) =>
            (byte)((hairStyle & 0x03)
                | ((hairColor & 0x03) << 2)
                | ((faceStyle & 0x03) << 4)
                | ((skinTone & 0x03) << 6));
    }
}

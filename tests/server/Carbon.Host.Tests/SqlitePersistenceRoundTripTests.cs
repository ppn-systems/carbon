// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Carbon.Abstractions.Identity;
using Carbon.Abstractions.Persistence;
using Carbon.Application.Identity;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class SqlitePersistenceRoundTripTests
{
    [Fact]
    public async Task RegisterLoginCharactersAndChunkDeltas_ShouldRoundTripThroughSqlite()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-host-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            SqliteSchemaMigrator migrator = new(factory);
            await migrator.MigrateAsync().ConfigureAwait(true);
            await migrator.MigrateAsync().ConfigureAwait(true);

            SqliteAccountStore accounts = new(factory);
            SqliteCharacterStore characters = new(factory);
            SqliteChunkDeltaRepository chunks = new(factory);
            Pbkdf2PasswordHasher hasher = new();
            using AuthService auth = new(accounts, characters, hasher);

            AuthOutcome registered = await auth.RegisterAsync("alice", "secret-pass", CarbonProtocol.Version).ConfigureAwait(true);
            registered.Status.Should().Be(AuthStatus.Ok);
            registered.AccountId.Should().NotBe(Guid.Empty);

            AuthOutcome duplicate = await auth.RegisterAsync("alice", "secret-pass", CarbonProtocol.Version).ConfigureAwait(true);
            duplicate.Status.Should().Be(AuthStatus.AccountExists);

            AuthOutcome badPass = await auth.LoginAsync("alice", "wrong-pass", CarbonProtocol.Version).ConfigureAwait(true);
            badPass.Status.Should().Be(AuthStatus.InvalidCredentials);

            AuthOutcome badVersion = await auth.LoginAsync("alice", "secret-pass", 999).ConfigureAwait(true);
            badVersion.Status.Should().Be(AuthStatus.ProtocolMismatch);

            AuthOutcome loggedIn = await auth.LoginAsync("alice", "secret-pass", CarbonProtocol.Version).ConfigureAwait(true);
            loggedIn.Status.Should().Be(AuthStatus.Ok);
            loggedIn.AccountId.Should().Be(registered.AccountId);

            byte packedPreset = CharacterAppearance.Pack(2, 3, 1, 2);
            CharacterCreateResult created = await auth.CreateCharacterAsync(loggedIn.AccountId, "Ranger", packedPreset, 128f, 256f).ConfigureAwait(true);
            created.Succeeded.Should().BeTrue();
            created.Character.Should().NotBeNull();

            CharacterRecord progressed = created.Character! with
            {
                Level = 4,
                Experience = 240U,
                CurrentMana = 62f,
                MaxMana = 110f,
                CurrentStamina = 74f,
                MaxStamina = 118f,
                StatusEffects = "v2;poisoned:4000:2:3;rested:600000:1:1",
            };
            await characters.SaveBatchAsync([progressed]).ConfigureAwait(true);

            IReadOnlyList<CharacterRecord> roster = await auth.ListCharactersAsync(loggedIn.AccountId).ConfigureAwait(true);
            roster.Should().ContainSingle(c =>
                c.Name == "Ranger" &&
                c.AppearancePreset == packedPreset &&
                c.Level == 4 &&
                c.Experience == 240U &&
                c.CurrentMana == 62f &&
                c.MaxMana == 110f &&
                c.CurrentStamina == 74f &&
                c.MaxStamina == 118f &&
                c.StatusEffects == "v2;poisoned:4000:2:3;rested:600000:1:1");

            CharacterRecord? unauthorizedLookup = await auth.FindOwnedCharacterAsync(Guid.NewGuid(), created.Character!.Id).ConfigureAwait(true);
            unauthorizedLookup.Should().BeNull();

            byte[] payload = [1, 2, 3, 4, 42];
            PersistedChunkDelta delta = new(3, -2, 7U, 12, payload, DateTimeOffset.UtcNow);
            await chunks.SaveBatchAsync([delta]).ConfigureAwait(true);

            IReadOnlyList<PersistedChunkDelta> loaded = await chunks.LoadAllAsync().ConfigureAwait(true);
            loaded.Should().ContainSingle(d => d.ChunkX == 3 && d.ChunkY == -2 && d.Revision == 7U && d.Payload.Length == 5);
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

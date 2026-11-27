// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using Carbon.Abstractions.Identity;
using Carbon.Application.Identity;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// The stored PBKDF2 password hash end to end: register, then sign in through <see cref="AuthService"/> on a real
/// SQLite account store, with the stored blob tampered with directly. Written against the byte[] hash, before it became
/// a <c>Bytes32</c>, and kept unchanged across that change.
/// </summary>
public sealed class PasswordHashStorageTests : IDisposable
{
    private const string Password = "correct horse";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"carbon-pwhash-{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _factory;
    private readonly AuthService _auth;

    public PasswordHashStorageTests()
    {
        _factory = new SqliteConnectionFactory($"Data Source={_dbPath}");
        new SqliteSchemaMigrator(_factory).MigrateAsync().GetAwaiter().GetResult();
        _auth = new AuthService(new SqliteAccountStore(_factory), new SqliteCharacterStore(_factory), new Pbkdf2PasswordHasher());
    }

    public void Dispose()
    {
        _auth.Dispose();
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task Login_RightPassword_SignsIn()
    {
        Guid id = await this.RegisterAsync("ana").ConfigureAwait(true);

        AuthOutcome login = await _auth.LoginAsync("ana", Password, CarbonProtocol.Version).ConfigureAwait(true);

        login.Status.Should().Be(AuthStatus.Ok);
        login.AccountId.Should().Be(id);
    }

    [Fact]
    public async Task Login_WrongPassword_IsRejected()
    {
        _ = await this.RegisterAsync("ben").ConfigureAwait(true);

        AuthOutcome login = await _auth.LoginAsync("ben", "correct horsf", CarbonProtocol.Version).ConfigureAwait(true);

        login.Status.Should().Be(AuthStatus.InvalidCredentials);
    }

    [Fact]
    public async Task Register_StoresA32ByteHashAndA16ByteSaltAsBlobs()
    {
        _ = await this.RegisterAsync("cai").ConfigureAwait(true);

        (byte[] hash, byte[] salt) = await this.ReadStoredAsync("cai").ConfigureAwait(true);

        hash.Should().HaveCount(32, "PBKDF2-SHA256 derives a 32-byte key");
        salt.Should().HaveCount(16);
    }

    [Fact]
    public async Task Register_SamePasswordTwice_StoresDifferentHashes()
    {
        _ = await this.RegisterAsync("dee").ConfigureAwait(true);
        _ = await this.RegisterAsync("eli").ConfigureAwait(true);

        (byte[] first, _) = await this.ReadStoredAsync("dee").ConfigureAwait(true);
        (byte[] second, _) = await this.ReadStoredAsync("eli").ConfigureAwait(true);

        first.Should().NotEqual(second, "each account gets its own random salt");
    }

    [Fact]
    public async Task Login_StoredHashOneByteWrongSameLength_IsRejected()
    {
        _ = await this.RegisterAsync("fay").ConfigureAwait(true);
        (byte[] hash, _) = await this.ReadStoredAsync("fay").ConfigureAwait(true);
        hash[17] ^= 0x01;
        await this.WriteStoredHashAsync("fay", hash).ConfigureAwait(true);

        AuthOutcome login = await _auth.LoginAsync("fay", Password, CarbonProtocol.Version).ConfigureAwait(true);

        login.Status.Should().Be(AuthStatus.InvalidCredentials, "a hash that differs in one bit must not verify");
    }

    [Theory]
    [InlineData(31)]
    [InlineData(0)]
    [InlineData(64)]
    public async Task Login_StoredHashOfTheWrongLength_IsRejectedNotThrown(int length)
    {
        _ = await this.RegisterAsync("gus").ConfigureAwait(true);
        (byte[] hash, _) = await this.ReadStoredAsync("gus").ConfigureAwait(true);
        byte[] damaged = new byte[length];
        hash.AsSpan(0, Math.Min(length, hash.Length)).CopyTo(damaged);
        await this.WriteStoredHashAsync("gus", damaged).ConfigureAwait(true);

        AuthOutcome login = await _auth.LoginAsync("gus", Password, CarbonProtocol.Version).ConfigureAwait(true);

        login.Status.Should().Be(AuthStatus.InvalidCredentials, "a damaged stored hash fails the sign-in instead of crashing it");
    }

    private async Task<Guid> RegisterAsync(string username)
    {
        AuthOutcome registered = await _auth.RegisterAsync(username, Password, CarbonProtocol.Version).ConfigureAwait(true);
        registered.Status.Should().Be(AuthStatus.Ok);
        return registered.AccountId;
    }

    private async Task<(byte[] Hash, byte[] Salt)> ReadStoredAsync(string username)
    {
        SqliteConnection connection = await _factory.OpenAsync().ConfigureAwait(true);
        await using (connection.ConfigureAwait(true))
        {
            SqliteCommand cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(true))
            {
                cmd.CommandText = "SELECT password_hash, password_salt FROM accounts WHERE username = @u";
                _ = cmd.Parameters.AddWithValue("@u", username);
                SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(true);
                await using (reader.ConfigureAwait(true))
                {
                    (await reader.ReadAsync().ConfigureAwait(true)).Should().BeTrue();
                    return ((byte[])reader["password_hash"], (byte[])reader["password_salt"]);
                }
            }
        }
    }

    private async Task WriteStoredHashAsync(string username, byte[] hash)
    {
        SqliteConnection connection = await _factory.OpenAsync().ConfigureAwait(true);
        await using (connection.ConfigureAwait(true))
        {
            SqliteCommand cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(true))
            {
                cmd.CommandText = "UPDATE accounts SET password_hash = @h WHERE username = @u";
                _ = cmd.Parameters.AddWithValue("@h", hash);
                _ = cmd.Parameters.AddWithValue("@u", username);
                (await cmd.ExecuteNonQueryAsync().ConfigureAwait(true)).Should().Be(1);
            }
        }
    }
}

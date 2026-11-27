// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Identity;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.Security;
using Carbon.Application.Identity;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>Remember-me tokens (ADR-037): issue, sign in, expire, revoke, and store only a hash.</summary>
public sealed class RememberTokenTests
{
    [Fact]
    public async Task LoginWithRememberTokenAsync_WithIssuedToken_SignsInTheAccount()
    {
        Harness h = new();
        using AuthService auth = h.CreateService();

        RememberTokenIssue issued = await auth.IssueRememberTokenAsync(h.Account.Id).ConfigureAwait(true);
        AuthOutcome outcome = await auth.LoginWithRememberTokenAsync(issued.Token, CarbonProtocol.Version).ConfigureAwait(true);

        issued.Token.Should().HaveCount(RememberTokenIssue.TokenLength);
        issued.ExpiresAtUtc.Should().Be(h.Time.GetUtcNow() + AuthService.RememberTokenLifetime);
        outcome.Status.Should().Be(AuthStatus.Ok, "a freshly issued token signs in");
        outcome.AccountId.Should().Be(h.Account.Id);
    }

    [Fact]
    public async Task IssueRememberTokenAsync_StoresOnlyTheSha256OfTheToken()
    {
        Harness h = new();
        using AuthService auth = h.CreateService();

        RememberTokenIssue issued = await auth.IssueRememberTokenAsync(h.Account.Id).ConfigureAwait(true);

        RememberTokenRecord stored = h.Tokens.Snapshot().Should().ContainSingle().Subject;
        stored.TokenHash.Should().Equal(SHA256.HashData(issued.Token), "the server keeps the hash, never the token");
        stored.TokenHash.Should().NotEqual(issued.Token);
    }

    [Fact]
    public async Task LoginWithRememberTokenAsync_AfterRevoke_RejectsTheToken()
    {
        Harness h = new();
        using AuthService auth = h.CreateService();
        RememberTokenIssue issued = await auth.IssueRememberTokenAsync(h.Account.Id).ConfigureAwait(true);

        await auth.RevokeRememberTokenAsync(issued.Token).ConfigureAwait(true);
        AuthOutcome outcome = await auth.LoginWithRememberTokenAsync(issued.Token, CarbonProtocol.Version).ConfigureAwait(true);

        outcome.Status.Should().Be(AuthStatus.InvalidCredentials, "log out revokes the token on the server");
        h.Tokens.Count.Should().Be(0);
    }

    [Fact]
    public async Task LoginWithRememberTokenAsync_AfterLifetime_RejectsAndDeletesTheToken()
    {
        Harness h = new();
        using AuthService auth = h.CreateService();
        RememberTokenIssue issued = await auth.IssueRememberTokenAsync(h.Account.Id).ConfigureAwait(true);

        h.Time.Advance(AuthService.RememberTokenLifetime);
        AuthOutcome outcome = await auth.LoginWithRememberTokenAsync(issued.Token, CarbonProtocol.Version).ConfigureAwait(true);

        outcome.Status.Should().Be(AuthStatus.InvalidCredentials, "an expired token must type the password again");
        h.Tokens.Count.Should().Be(0, "the expired row is cleaned up");
    }

    [Fact]
    public async Task LoginWithRememberTokenAsync_ForBannedAccount_ReturnsLocked()
    {
        Harness h = new();
        using AuthService auth = h.CreateService();
        RememberTokenIssue issued = await auth.IssueRememberTokenAsync(h.Account.Id).ConfigureAwait(true);
        h.Accounts.FindByIdAsync(h.Account.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AccountRecord?>(h.Account with { IsBanned = true }));

        AuthOutcome outcome = await auth.LoginWithRememberTokenAsync(issued.Token, CarbonProtocol.Version).ConfigureAwait(true);

        outcome.Status.Should().Be(AuthStatus.Locked, "a ban applies to remembered devices too");
    }

    [Fact]
    public async Task LoginWithRememberTokenAsync_WithUnknownOrMalformedToken_RejectsIt()
    {
        Harness h = new();
        using AuthService auth = h.CreateService();
        _ = await auth.IssueRememberTokenAsync(h.Account.Id).ConfigureAwait(true);

        (await auth.LoginWithRememberTokenAsync(RandomNumberGenerator.GetBytes(RememberTokenIssue.TokenLength), CarbonProtocol.Version).ConfigureAwait(true))
            .Status.Should().Be(AuthStatus.InvalidCredentials);
        (await auth.LoginWithRememberTokenAsync([1, 2, 3], CarbonProtocol.Version).ConfigureAwait(true))
            .Status.Should().Be(AuthStatus.InvalidCredentials);
        (await auth.LoginWithRememberTokenAsync([], CarbonProtocol.Version).ConfigureAwait(true))
            .Status.Should().Be(AuthStatus.InvalidCredentials);
    }

    [Fact]
    public async Task LoginWithRememberTokenAsync_WithOldClientVersion_ReturnsProtocolMismatch()
    {
        Harness h = new();
        using AuthService auth = h.CreateService();
        RememberTokenIssue issued = await auth.IssueRememberTokenAsync(h.Account.Id).ConfigureAwait(true);

        AuthOutcome outcome = await auth.LoginWithRememberTokenAsync(issued.Token, 999).ConfigureAwait(true);

        outcome.Status.Should().Be(AuthStatus.ProtocolMismatch);
    }

    [Fact]
    public async Task IssueRememberTokenAsync_BeyondThePerAccountCap_DropsTheOldestToken()
    {
        Harness h = new();
        using AuthService auth = h.CreateService();
        RememberTokenIssue oldest = await auth.IssueRememberTokenAsync(h.Account.Id).ConfigureAwait(true);
        for (int i = 0; i < AuthService.MaxRememberTokensPerAccount; i++)
        {
            _ = await auth.IssueRememberTokenAsync(h.Account.Id).ConfigureAwait(true);
        }

        h.Tokens.Count.Should().Be(AuthService.MaxRememberTokensPerAccount, "asking again and again must not grow the table");
        (await auth.LoginWithRememberTokenAsync(oldest.Token, CarbonProtocol.Version).ConfigureAwait(true))
            .Status.Should().Be(AuthStatus.InvalidCredentials, "the oldest device is forgotten first");
    }

    [Fact]
    public async Task SqliteRememberTokenStore_IssueSignInRevoke_RoundTripsThroughMigration0020()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-remember-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            SqliteRememberTokenStore tokens = new(factory);
            using AuthService auth = new(new SqliteAccountStore(factory), new SqliteCharacterStore(factory), new Pbkdf2PasswordHasher(), rememberTokens: tokens);
            AuthOutcome registered = await auth.RegisterAsync("remembered", "secret-pass", CarbonProtocol.Version).ConfigureAwait(true);

            RememberTokenIssue first = await auth.IssueRememberTokenAsync(registered.AccountId).ConfigureAwait(true);
            RememberTokenIssue second = await auth.IssueRememberTokenAsync(registered.AccountId).ConfigureAwait(true);
            AuthOutcome signedIn = await auth.LoginWithRememberTokenAsync(first.Token, CarbonProtocol.Version).ConfigureAwait(true);
            await auth.RevokeRememberTokenAsync(first.Token).ConfigureAwait(true);
            AuthOutcome afterRevoke = await auth.LoginWithRememberTokenAsync(first.Token, CarbonProtocol.Version).ConfigureAwait(true);
            AuthOutcome otherDevice = await auth.LoginWithRememberTokenAsync(second.Token, CarbonProtocol.Version).ConfigureAwait(true);

            signedIn.Status.Should().Be(AuthStatus.Ok);
            signedIn.AccountId.Should().Be(registered.AccountId);
            afterRevoke.Status.Should().Be(AuthStatus.InvalidCredentials, "the revoked row is deleted");
            otherDevice.Status.Should().Be(AuthStatus.Ok, "revoking one device leaves the others signed in");
            (await tokens.FindAsync(SHA256.HashData(second.Token)).ConfigureAwait(true)).Should().NotBeNull();
            (await File.ReadAllBytesAsync(dbPath).ConfigureAwait(true)).AsSpan().IndexOf(second.Token).Should().Be(-1, "the database holds only token hashes");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task SqliteRememberTokenStore_BeyondThePerAccountCap_DropsTheOldestRow()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-remember-cap-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            using AuthService auth = new(new SqliteAccountStore(factory), new SqliteCharacterStore(factory), new Pbkdf2PasswordHasher(), rememberTokens: new SqliteRememberTokenStore(factory));
            AuthOutcome registered = await auth.RegisterAsync("many_devices", "secret-pass", CarbonProtocol.Version).ConfigureAwait(true);
            RememberTokenIssue oldest = await auth.IssueRememberTokenAsync(registered.AccountId).ConfigureAwait(true);
            RememberTokenIssue newest = oldest;
            for (int i = 0; i < AuthService.MaxRememberTokensPerAccount; i++)
            {
                newest = await auth.IssueRememberTokenAsync(registered.AccountId).ConfigureAwait(true);
            }

            (await auth.LoginWithRememberTokenAsync(oldest.Token, CarbonProtocol.Version).ConfigureAwait(true))
                .Status.Should().Be(AuthStatus.InvalidCredentials, "the database keeps only the newest tokens per account");
            (await auth.LoginWithRememberTokenAsync(newest.Token, CarbonProtocol.Version).ConfigureAwait(true))
                .Status.Should().Be(AuthStatus.Ok);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    private sealed class Harness
    {
        public Harness()
        {
            this.Account = new AccountRecord(Guid.NewGuid(), "hero", "HERO", [1], [2], this.Time.GetUtcNow(), IsBanned: false);
            this.Accounts.FindByIdAsync(this.Account.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<AccountRecord?>(this.Account));
        }

        public AccountRecord Account { get; }

        public IAccountStore Accounts { get; } = Substitute.For<IAccountStore>();

        public InMemoryRememberTokenStore Tokens { get; } = new();

        public ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));

        public AuthService CreateService() =>
            new(this.Accounts, Substitute.For<ICharacterStore>(), Substitute.For<IPasswordHasher>(), this.Time, rememberTokens: this.Tokens);
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Identity;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.Security;
using Carbon.Application.Identity;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class AuthServiceLoginThrottlingTests
{
    private const string Username = "hero";
    private const string Password = "correct-horse";

    [Fact]
    public async Task LoginAsync_AfterMaxFailedAttempts_LocksAccountWithoutHashing()
    {
        Harness h = new();
        using AuthService auth = h.CreateService();

        for (int i = 0; i < AuthService.MaxFailedLoginsPerWindow; i++)
        {
            (await auth.LoginAsync(Username, "wrong", CarbonProtocol.Version).ConfigureAwait(true)).Status
                .Should().Be(AuthStatus.InvalidCredentials);
        }

        AuthOutcome locked = await auth.LoginAsync(Username, Password, CarbonProtocol.Version).ConfigureAwait(true);

        locked.Status.Should().Be(AuthStatus.Locked, "even the correct password is refused while the account is locked");
        h.Hasher.VerifyCalls.Should().Be(AuthService.MaxFailedLoginsPerWindow, "a locked account is rejected before hashing");
    }

    [Fact]
    public async Task LoginAsync_AfterLockoutWindowElapses_AcceptsCorrectPassword()
    {
        Harness h = new();
        using AuthService auth = h.CreateService();
        for (int i = 0; i < AuthService.MaxFailedLoginsPerWindow; i++)
        {
            _ = await auth.LoginAsync(Username, "wrong", CarbonProtocol.Version).ConfigureAwait(true);
        }

        h.Time.Advance(AuthService.LoginFailureWindow);

        (await auth.LoginAsync(Username, Password, CarbonProtocol.Version).ConfigureAwait(true)).Status
            .Should().Be(AuthStatus.Ok);
    }

    [Fact]
    public async Task LoginAsync_SuccessfulLogin_ResetsFailureCount()
    {
        Harness h = new();
        using AuthService auth = h.CreateService();
        for (int i = 0; i < AuthService.MaxFailedLoginsPerWindow - 1; i++)
        {
            _ = await auth.LoginAsync(Username, "wrong", CarbonProtocol.Version).ConfigureAwait(true);
        }

        (await auth.LoginAsync(Username, Password, CarbonProtocol.Version).ConfigureAwait(true)).Status.Should().Be(AuthStatus.Ok);
        (await auth.LoginAsync(Username, "wrong", CarbonProtocol.Version).ConfigureAwait(true)).Status
            .Should().Be(AuthStatus.InvalidCredentials, "the earlier failures were cleared by the successful login");
    }

    [Fact]
    public async Task LoginAsync_PasswordLongerThanCap_ReturnsInvalidInputWithoutHashing()
    {
        Harness h = new();
        using AuthService auth = h.CreateService();

        AuthOutcome outcome = await auth
            .LoginAsync(Username, new string('x', AuthService.MaxPasswordLength + 1), CarbonProtocol.Version)
            .ConfigureAwait(true);

        outcome.Status.Should().Be(AuthStatus.InvalidInput);
        h.Hasher.VerifyCalls.Should().Be(0);
    }

    private sealed class Harness
    {
        public Harness()
        {
            AccountRecord account = new(
                Guid.NewGuid(),
                Username,
                Username.ToUpperInvariant(),
                [1],
                [2],
                this.Time.GetUtcNow(),
                IsBanned: false);
            this.Accounts.FindByUsernameAsync(Username.ToUpperInvariant(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<AccountRecord?>(account));
        }

        public IAccountStore Accounts { get; } = Substitute.For<IAccountStore>();

        public CountingHasher Hasher { get; } = new();

        public ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));

        public AuthService CreateService() => new(this.Accounts, Substitute.For<ICharacterStore>(), this.Hasher, this.Time);
    }

    /// <summary>Accepts only <see cref="Password"/> and counts verifications.</summary>
    private sealed class CountingHasher : IPasswordHasher
    {
        private int _verifyCalls;

        public int VerifyCalls => Volatile.Read(ref _verifyCalls);

        public void HashPassword(string password, out byte[] hash, out byte[] salt)
        {
            hash = [1];
            salt = [2];
        }

        public bool Verify(string password, ReadOnlySpan<byte> expectedHash, ReadOnlySpan<byte> salt)
        {
            _ = Interlocked.Increment(ref _verifyCalls);
            return password == Password;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

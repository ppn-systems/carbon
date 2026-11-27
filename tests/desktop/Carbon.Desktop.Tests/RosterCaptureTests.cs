// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Capture;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>The roster screenshot scenario seeds two distinct heroes, and only for an account it could register.</summary>
public sealed class RosterCaptureTests
{
    private static readonly GameServer s_server = new("local", "Home Realm", "127.0.0.1", 57207);

    [Fact]
    public async Task RegisterWithHeroesAsync_AfterRegistering_CreatesTwoHeroesWithDifferentLooks()
    {
        IWorldSession session = Substitute.For<IWorldSession>();
        session.RegisterAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(new AuthenticationResult(true, "ok"));
        session.CreateCharacterAsync(default!, default, default).ReturnsForAnyArgs((true, "Created"));

        AuthenticationResult result = await RosterCapture.RegisterWithHeroesAsync(session, s_server, new DesktopOptions()).ConfigureAwait(true);

        result.Succeeded.Should().BeTrue();
        await session.Received(1).CreateCharacterAsync("Ayla", new CharacterLook(1, 1, [2, 1, 2, 1]), Arg.Any<CancellationToken>()).ConfigureAwait(true);
        await session.Received(1).CreateCharacterAsync("Bram", new CharacterLook(1, 0, [1, 2, 1, 3]), Arg.Any<CancellationToken>()).ConfigureAwait(true);
    }

    [Fact]
    public async Task RegisterWithHeroesAsync_WhenRegistrationFails_CreatesNoHero()
    {
        IWorldSession session = Substitute.For<IWorldSession>();
        session.RegisterAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(new AuthenticationResult(false, "taken"));

        AuthenticationResult result = await RosterCapture.RegisterWithHeroesAsync(session, s_server, new DesktopOptions()).ConfigureAwait(true);

        result.Succeeded.Should().BeFalse("the failure is passed on so the start screen reports it");
        await session.DidNotReceiveWithAnyArgs().CreateCharacterAsync(default!, default, default).ConfigureAwait(true);
    }
}

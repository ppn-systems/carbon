// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Networking.Session;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Scenes;
using Carbon.Desktop.Tests.Fakes;
using Carbon.Graphics.Batching.Common;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// ADR-031 client side: the server's <see cref="PlayerDeath"/> push opens the death screen, and Respawn (Enter or the
/// button) sends exactly one <c>RespawnRequest</c> until the server answers.
/// </summary>
public sealed class DeathScreenTests
{
    private const int Width = 1280;
    private const int Height = 720;

    [Fact]
    public void OnPlayerDeath_MonsterKill_OpensTheDeathScreenWithKillerAndGold()
    {
        DeathSession death = new(() => Task.FromResult(OpStatus.Ok), id => id == 40 ? "monster.wild_slime.name" : null);
        death.Death.Should().BeNull("a living player sees no death screen");

        using PlayerDeath packet = new() { Cause = DeathCause.Monster, KillerArchetypeId = 40, KillerNetworkId = 9_001UL, GoldDropped = 500L };
        death.OnPlayerDeath(packet);

        death.Death.Should().Be(new DeathView(DeathCause.Monster, 40, 500L, "monster.wild_slime.name"));
    }

    [Fact]
    public void TryRequestRespawn_PressedRepeatedlyBeforeTheAnswer_SendsExactlyOneRequest()
    {
        TaskCompletionSource<OpStatus> answer = new();
        int sent = 0;
        DeathSession death = Dead(() =>
        {
            sent++;
            return answer.Task;
        });

        death.TryRequestRespawn().Should().BeTrue();
        death.TryRequestRespawn().Should().BeFalse("a request is already waiting for the server");
        death.TryRequestRespawn().Should().BeFalse();

        sent.Should().Be(1);
        death.IsRespawnPending.Should().BeTrue();
        answer.SetResult(OpStatus.Ok);
        SpinWait.SpinUntil(() => death.Death is null, TimeSpan.FromSeconds(5)).Should().BeTrue("the server accepted the respawn, so the death screen closes");
        death.IsRespawnPending.Should().BeFalse();
    }

    [Fact]
    public void TryRequestRespawn_ServerRefuses_KeepsTheScreenOpenAndAllowsAnotherTry()
    {
        int sent = 0;
        DeathSession death = Dead(() =>
        {
            sent++;
            return Task.FromResult(OpStatus.Forbidden);
        });

        death.TryRequestRespawn().Should().BeTrue();
        SpinWait.SpinUntil(() => !death.IsRespawnPending, TimeSpan.FromSeconds(5)).Should().BeTrue();
        death.Death.Should().NotBeNull("only the server decides the player is alive again");
        death.TryRequestRespawn().Should().BeTrue("a refused request may be retried");

        sent.Should().Be(2);
    }

    [Fact]
    public void TryRequestRespawn_WhileAlive_SendsNothing()
    {
        int sent = 0;
        DeathSession death = new(() =>
        {
            sent++;
            return Task.FromResult(OpStatus.Ok);
        }, _ => null);

        death.TryRequestRespawn().Should().BeFalse();
        sent.Should().Be(0);
    }

    [Fact]
    public async Task Handle_EnterOnThreeFrames_SendsExactlyOneRespawnRequest()
    {
        int sent = 0;
        DeathSession death = Dead(() =>
        {
            sent++;
            return new TaskCompletionSource<OpStatus>().Task;
        });
        RecordingWorldSession session = new() { LocalDeath = death.Death, RespawnHandler = death.TryRequestRespawn };
        await using ConfiguredAsyncDisposable scope = session.ConfigureAwait(true);

        for (int frame = 0; frame < 3; frame++)
        {
            DeathScreenInput.Handle(session, confirmPressed: true, leftClickPressed: false, Vector2.Zero, Width, Height)
                .Should().BeTrue("the death screen swallows gameplay input");
        }

        sent.Should().Be(1, "Respawn sends one request until the server answers");
    }

    [Fact]
    public async Task Handle_ClickOnRespawnButton_RequestsRespawn_ClickElsewhere_DoesNot()
    {
        RecordingWorldSession session = new() { LocalDeath = new DeathView(DeathCause.Starvation, 0, 0L) };
        await using ConfiguredAsyncDisposable scope = session.ConfigureAwait(true);
        RectangleF button = HudOverlayRenderer.GetRespawnButtonRect(Width, Height);

        DeathScreenInput.Handle(session, confirmPressed: false, leftClickPressed: true, new Vector2(10f, 10f), Width, Height).Should().BeTrue();
        session.Commands.Should().BeEmpty("a click outside the button does nothing");

        Vector2 center = new(button.X + (button.Width * 0.5f), button.Y + (button.Height * 0.5f));
        DeathScreenInput.Handle(session, confirmPressed: false, leftClickPressed: true, center, Width, Height).Should().BeTrue();
        session.Commands.Should().Equal("Respawn()");
    }

    [Fact]
    public async Task Handle_Alive_LeavesInputToTheGameAndSendsNothing()
    {
        RecordingWorldSession session = new();
        await using ConfiguredAsyncDisposable scope = session.ConfigureAwait(true);

        DeathScreenInput.Handle(session, confirmPressed: true, leftClickPressed: true, Vector2.Zero, Width, Height).Should().BeFalse();

        session.Commands.Should().BeEmpty();
    }

    private static DeathSession Dead(Func<Task<OpStatus>> send)
    {
        DeathSession death = new(send, _ => null);
        using PlayerDeath packet = new() { Cause = DeathCause.Monster, KillerArchetypeId = 40, GoldDropped = 100L };
        death.OnPlayerDeath(packet);
        return death;
    }
}

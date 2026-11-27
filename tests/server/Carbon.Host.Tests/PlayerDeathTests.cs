// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// The lethal branch of the server's player damage path (<c>AuthoritativeWorld.ApplyDamageToPlayerLocked</c>):
/// a player killed by a monster stays dead until they ask to respawn (ADR-031), then respawns at the sanctuary with full
/// health, mana and stamina.
/// </summary>
public sealed class PlayerDeathTests
{
    private const int MaxTicksToDie = 1_200;
    private const ulong EntropyLabel = 1UL;

    [Fact]
    public async Task MonsterKillsPlayer_RespawnRequest_RespawnsAtSanctuaryWithFullResources()
    {
        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        // Fixed clock and gameplay secret: combat rolls (dodge, crits, AI) are keyed by them (ADR-026), so the fight
        // plays out identically on every run.
        AuthoritativeWorld world = new(
            1337UL,
            32f,
            0.0008f,
            30,
            Substitute.For<IChunkDeltaRepository>(),
            Substitute.For<ICharacterStore>(),
            sink,
            new FixedTime(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000L)),
            entropy: TestGameplayEntropy.Create(EntropyLabel));
        EntitySpawnSnapshot mob = FindMonster(world, sink);

        const ulong victimConn = 900UL;
        CharacterRecord victim = Character("Victim") with
        {
            PositionX = mob.PositionX + 12f,
            PositionY = mob.PositionY,
            CurrentHealth = 1f,
            CurrentMana = 3f,
            CurrentStamina = 2f,
        };
        EnterWorldResult entered = world.TryEnterWorld(victimConn, victim);
        entered.Status.Should().Be(OpStatus.Ok);

        // ADR-033: a 4-stamina punch at 2 stamina is refused and exhausts the victim before the fight ends.
        world.EnqueueAttackInput(victimConn, mob.NetworkId, new Vector2(mob.PositionX, mob.PositionY), 0).Should().Be(OpStatus.Ok);
        world.StepFixedTick();
        sink.StaminaStatuses.Should().ContainSingle(s => s.ConnectionId == victimConn && s.Exhausted);

        bool died = false;
        for (int tick = 0; tick < MaxTicksToDie && !died; tick++)
        {
            world.StepFixedTick();
            died = sink.CombatHits.Any(h => h.DefenderNetworkId == entered.PlayerNetworkId && h.IsKilled);
        }

        died.Should().BeTrue("a 1 HP player standing next to a monster must eventually be killed");
        world.StepFixedTick();
        world.TryGetPendingCharacterSnapshot(victim.Id, out CharacterRecord? dead).Should().BeTrue();
        dead!.CurrentHealth.Should().Be(0f, "there is no auto-respawn (ADR-031)");

        float staminaAtDeath = dead.CurrentStamina;
        for (int tick = 0; tick < 90; tick++)
        {
            world.StepFixedTick();
        }

        world.TryGetPendingCharacterSnapshot(victim.Id, out CharacterRecord? stillDead).Should().BeTrue();
        stillDead!.CurrentStamina.Should().Be(staminaAtDeath, "a dead player does not regenerate stamina (ADR-033)");

        ValueTask<WorldMutationOutcome> respawn = world.SubmitRespawnAsync(victimConn);
        world.StepFixedTick();
        (await respawn.ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
        world.StepFixedTick(); // the sanctuary teleport is applied by the next simulation step

        world.TryGetPendingCharacterSnapshot(victim.Id, out CharacterRecord? after).Should().BeTrue("the character took damage, so its state is pending a save");
        after!.CurrentHealth.Should().Be(after.MaxHealth, "respawn heals to full");
        after.CurrentMana.Should().Be(after.MaxMana, "respawn refills mana");
        after.CurrentStamina.Should().Be(after.MaxStamina, "respawn refills stamina");
        sink.StaminaStatuses.Last(s => s.ConnectionId == victimConn).Exhausted.Should().BeFalse("respawn clears exhaustion");
        Vector2 respawnedAt = new(after.PositionX, after.PositionY);
        Vector2.Distance(respawnedAt, world.DefaultSpawnWorldPosition).Should().BeLessThan(
            world.TileSize * 2f, "the dead player is moved to the sanctuary, not left where they fell");
        Vector2.Distance(respawnedAt, new Vector2(mob.PositionX, mob.PositionY)).Should().BeGreaterThan(
            world.TileSize * 4f, "respawning next to the monster that killed you would be a death loop");
    }

    private static EntitySpawnSnapshot FindMonster(AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink)
    {
        for (int probeX = 1; probeX <= 12; probeX++)
        {
            ulong probeConn = 700UL + (ulong)probeX;
            world.TryEnterWorld(probeConn, Character($"Probe{probeX}") with
            {
                PositionX = ((probeX * 32) + 16) * 32f,
                PositionY = 16f * 32f,
            }).Status.Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            EntitySpawnSnapshot? mob = sink.Spawns
                .Where(x => x.ConnectionId == probeConn && x.Snapshot.Kind == EntityKind.Monster)
                .Select(x => (EntitySpawnSnapshot?)x.Snapshot)
                .FirstOrDefault();
            if (mob is not null)
            {
                return mob.Value;
            }
        }

        throw new InvalidOperationException("wilderness chunks should populate monsters");
    }

    private static CharacterRecord Character(string name) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

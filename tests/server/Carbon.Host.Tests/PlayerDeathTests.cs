// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Numerics;
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
/// a player killed by a monster respawns at the sanctuary with full health, mana and stamina.
/// </summary>
public sealed class PlayerDeathTests
{
    private const int MaxTicksToDie = 1_200;

    [Fact]
    public void MonsterKillsPlayer_RespawnsAtSanctuaryWithFullResources()
    {
        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        AuthoritativeWorld world = new(
            1337UL, 32f, 0.0008f, 30, Substitute.For<IChunkDeltaRepository>(), Substitute.For<ICharacterStore>(), sink);
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
        EnterWorldResult entered = world.TryEnterWorld(victimConn, victimConn, victim);
        entered.Status.Should().Be(OpStatus.Ok);

        bool died = false;
        for (int tick = 0; tick < MaxTicksToDie && !died; tick++)
        {
            world.StepFixedTick();
            died = sink.CombatHits.Any(h => h.DefenderNetworkId == entered.PlayerNetworkId && h.IsKilled);
        }

        died.Should().BeTrue("a 1 HP player standing next to a monster must eventually be killed");
        world.StepFixedTick(); // the sanctuary teleport is applied by the next simulation step

        world.TryGetPendingCharacterSnapshot(victim.Id, out CharacterRecord? after).Should().BeTrue("the character took damage, so its state is pending a save");
        after!.CurrentHealth.Should().Be(after.MaxHealth, "respawn heals to full");
        after.CurrentMana.Should().Be(after.MaxMana, "respawn refills mana");
        after.CurrentStamina.Should().Be(after.MaxStamina, "respawn refills stamina");
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
            world.TryEnterWorld(probeConn, probeConn, Character($"Probe{probeX}") with
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
}

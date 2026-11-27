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
using Carbon.Economy;
using FluentAssertions;
using NSubstitute;
using Xunit;
using RecordingOutboundSink = Carbon.Host.Tests.AuthoritativeWorldMultiplayerTests.RecordingOutboundSink;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-031 end to end on <see cref="AuthoritativeWorld"/>: a monster kill leaves the player dead where they fell, unable
/// to act, until they send a respawn request; the respawn moves them to the sanctuary with full vitals.
/// </summary>
public sealed class PlayerDeathStateTests
{
    private const int MaxTicksToDie = 1_200;
    private const ulong VictimConn = 900UL;

    [Fact]
    public void MonsterKill_PlayerStaysDeadAtTheSpot_UntilARespawnRequest()
    {
        DeathFixture f = DeathFixture.KillVictim();

        for (int i = 0; i < 300; i++)
        {
            f.World.StepFixedTick();
        }

        CharacterRecord dead = f.Snapshot();
        dead.CurrentHealth.Should().Be(0f, "there is no auto-respawn: the player stays dead");
        Vector2.Distance(new Vector2(dead.PositionX, dead.PositionY), f.DeathPosition).Should().BeLessThan(f.World.TileSize, "the corpse stays where it fell");
        f.World.IsPlayerDead(VictimConn).Should().BeTrue();
        f.Sink.CombatHits.Skip(f.HitsAtDeath).Should().NotContain(h => h.DefenderNetworkId == f.VictimNetworkId, "monsters stop attacking a dead player");
    }

    [Fact]
    public async Task DeadPlayer_EveryActionIsRefused()
    {
        DeathFixture f = DeathFixture.KillVictim();

        f.World.EnqueueAttackInput(VictimConn, 0UL, f.DeathPosition + new Vector2(30f, 0f), 2).Should().Be(OpStatus.Forbidden);
        f.World.EnqueuePickupLoot(VictimConn, 12_345UL).Should().Be(OpStatus.Forbidden);
        ValueTask<WorldMutationOutcome> rest = f.World.SubmitSetRestingAsync(VictimConn, true);
        ValueTask<WorldMutationOutcome> sort = f.World.SubmitSortInventoryAsync(VictimConn);
        ValueTask<WorldMutationOutcome> craft = f.World.SubmitCraftRecipeAsync(VictimConn, 1, 1);
        for (int i = 0; i < 30; i++)
        {
            f.World.EnqueueMoveInput(VictimConn, (uint)(100 + i), Vector2.UnitX, sprint: true);
            f.World.StepFixedTick();
        }

        (await rest.ConfigureAwait(true)).Status.Should().Be(OpStatus.Forbidden);
        (await sort.ConfigureAwait(true)).Status.Should().Be(OpStatus.Forbidden);
        (await craft.ConfigureAwait(true)).Status.Should().Be(OpStatus.Forbidden);
        CharacterRecord dead = f.Snapshot();
        Vector2.Distance(new Vector2(dead.PositionX, dead.PositionY), f.DeathPosition).Should().BeLessThan(1f, "a dead player cannot move");
    }

    [Fact]
    public async Task RespawnRequest_WhileDead_MovesToTheSanctuaryWithFullVitals()
    {
        DeathFixture f = DeathFixture.KillVictim();

        ValueTask<WorldMutationOutcome> respawn = f.World.SubmitRespawnAsync(VictimConn);
        f.World.StepFixedTick();
        f.World.StepFixedTick(); // the sanctuary teleport is applied by the next simulation step

        (await respawn.ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
        f.World.IsPlayerDead(VictimConn).Should().BeFalse();
        CharacterRecord after = f.Snapshot();
        after.CurrentHealth.Should().Be(after.MaxHealth, "respawn heals to full");
        after.CurrentMana.Should().Be(after.MaxMana, "respawn refills mana");
        after.CurrentStamina.Should().Be(after.MaxStamina, "respawn refills stamina");
        Vector2.Distance(new Vector2(after.PositionX, after.PositionY), f.World.DefaultSpawnWorldPosition)
            .Should().BeLessThan(f.World.TileSize * 2f, "the respawn point is the sanctuary");
        f.World.EnqueueAttackInput(VictimConn, 0UL, Vector2.Zero, 2).Should().Be(OpStatus.Ok, "a respawned player can act again");
    }

    [Fact]
    public async Task RespawnRequest_WhileAlive_IsRejectedAndChangesNothing()
    {
        RecordingOutboundSink sink = new();
        AuthoritativeWorld world = DeathFixture.CreateWorld(sink);
        CharacterRecord hero = DeathFixture.Character("Alive") with { PositionX = 4_000f, PositionY = 4_000f, CurrentHealth = 40f };
        world.TryEnterWorld(VictimConn, hero).Status.Should().Be(OpStatus.Ok);

        ValueTask<WorldMutationOutcome> respawn = world.SubmitRespawnAsync(VictimConn);
        world.StepFixedTick();

        (await respawn.ConfigureAwait(true)).Status.Should().Be(OpStatus.Forbidden, "only a dead player may respawn");
        world.TryRespawn(VictimConn).Should().Be(OpStatus.Forbidden);
        world.TryGetPendingCharacterSnapshot(hero.Id, out CharacterRecord? after);
        (after ?? hero).CurrentHealth.Should().BeLessThan(100f, "a refused respawn must not heal the player");
    }

    [Fact]
    public void DeathPacket_IsSentOnceToTheVictim_WithTheKillingMonster()
    {
        DeathFixture f = DeathFixture.KillVictim();

        f.Sink.PlayerDeaths.Should().ContainSingle(d => d.ConnectionId == VictimConn);
        PlayerDeathSnapshot death = f.Sink.PlayerDeaths.Single().Death;
        death.Cause.Should().Be(DeathCause.Monster);
        death.KillerArchetypeId.Should().Be(f.Killer.ArchetypeId, "the client names the killer from its archetype id");
        death.GoldDropped.Should().Be(0L, "this world runs without an economy");
    }

    [Fact]
    public void MonsterKill_Level20With5000Gold_DropsTheDebitAsGroundPilesAndReportsIt()
    {
        DeathFixture f = DeathFixture.KillVictim(KillLootPickupTests.AlwaysGoldEconomy, level: 20, gold: 5_000L);
        f.World.StepFixedTick(); // the piles reach the victim's AoI on the next replication pass

        f.World.TryGetWalletBalances(VictimConn, out long gold, out _).Should().BeTrue();
        gold.Should().Be(4_500L, "10% of 5,000 at level 20");
        f.Sink.PlayerDeaths.Single().Death.GoldDropped.Should().Be(500L, "the death screen shows exactly the debit");
        long onGround = f.Sink.Spawns
            .Where(s => s.ConnectionId == VictimConn && s.Snapshot.Kind == EntityKind.LootDrop && s.Snapshot.LootCurrency == LootCurrency.Gold)
            .GroupBy(s => s.Snapshot.NetworkId)
            .Sum(g => (long)g.Last().Snapshot.LootCount);
        onGround.Should().Be(500L, "the coins dropped equal the gold debited: nothing is created or destroyed");

        for (int i = 0; i < 60; i++)
        {
            f.World.StepFixedTick();
        }

        f.World.TryGetWalletBalances(VictimConn, out long goldWhileDead, out _).Should().BeTrue();
        goldWhileDead.Should().Be(4_500L, "a corpse lying on its coins cannot auto-collect them");
    }

    [Fact]
    public void LogoutWhileDead_NextLogin_RespawnsAtTheSanctuaryWithFullVitals()
    {
        DeathFixture f = DeathFixture.KillVictim();
        CharacterRecord saved = f.Snapshot();
        f.World.LeaveWorld(VictimConn).Should().BeTrue();

        EnterWorldResult reentered = f.World.TryEnterWorld(901UL, saved);

        reentered.Status.Should().Be(OpStatus.Ok);
        reentered.CurrentHealth.Should().Be(reentered.MaxHealth, "a character saved dead respawns on its next login");
        reentered.CurrentMana.Should().Be(reentered.MaxMana);
        Vector2.Distance(new Vector2(reentered.PositionX, reentered.PositionY), f.World.DefaultSpawnWorldPosition)
            .Should().BeLessThan(1f, "it respawns at the sanctuary, not on its corpse");
        f.World.IsPlayerDead(901UL).Should().BeFalse();
    }

    private sealed class DeathFixture
    {
        private DeathFixture(AuthoritativeWorld world, RecordingOutboundSink sink, CharacterRecord victim)
        {
            this.World = world;
            this.Sink = sink;
            this.Victim = victim;
        }

        public AuthoritativeWorld World { get; }

        public RecordingOutboundSink Sink { get; }

        public CharacterRecord Victim { get; }

        public ulong VictimNetworkId { get; private set; }

        public EntitySpawnSnapshot Killer { get; private set; }

        public Vector2 DeathPosition { get; private set; }

        public int HitsAtDeath { get; private set; }

        public static AuthoritativeWorld CreateWorld(RecordingOutboundSink sink, GameEconomy? economy = null) =>
            new(
                1337UL,
                32f,
                0.0008f,
                30,
                Substitute.For<IChunkDeltaRepository>(),
                Substitute.For<ICharacterStore>(),
                sink,
                new FixedTime(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000L)),
                economy: economy,
                entropy: TestGameplayEntropy.Create(1UL));

        public static CharacterRecord Character(string name) =>
            new(Guid.NewGuid(), Guid.NewGuid(), name, 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);

        /// <summary>Puts a 1 HP player next to a wild monster and steps until the monster kills them.</summary>
        public static DeathFixture KillVictim(GameEconomy? economy = null, ushort level = 1, long gold = 0L)
        {
            RecordingOutboundSink sink = new();
            AuthoritativeWorld world = CreateWorld(sink, economy);
            EntitySpawnSnapshot mob = FindMonster(world, sink);
            CharacterRecord victim = Character("Victim") with
            {
                Level = level,
                Gold = gold,
                PositionX = mob.PositionX + 12f,
                PositionY = mob.PositionY,
                CurrentHealth = 1f,
                CurrentMana = 3f,
                CurrentStamina = 2f,
            };
            EnterWorldResult entered = world.TryEnterWorld(VictimConn, victim);
            entered.Status.Should().Be(OpStatus.Ok);
            DeathFixture f = new(world, sink, victim) { VictimNetworkId = entered.PlayerNetworkId, Killer = mob };

            for (int tick = 0; tick < MaxTicksToDie; tick++)
            {
                world.StepFixedTick();
                if (sink.CombatHits.Any(h => h.DefenderNetworkId == entered.PlayerNetworkId && h.IsKilled))
                {
                    f.HitsAtDeath = sink.CombatHits.Count;
                    CharacterRecord dead = f.Snapshot();
                    f.DeathPosition = new Vector2(dead.PositionX, dead.PositionY);
                    return f;
                }
            }

            throw new InvalidOperationException("a 1 HP player standing next to a monster must eventually be killed");
        }

        public CharacterRecord Snapshot()
        {
            this.World.TryGetPendingCharacterSnapshot(this.Victim.Id, out CharacterRecord? record).Should().BeTrue("the victim's state is pending a save");
            return record!;
        }

        private static EntitySpawnSnapshot FindMonster(AuthoritativeWorld world, RecordingOutboundSink sink)
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
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.Configuration;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Maps.Generators;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// Guards the owner's dawn report: monsters a player is fighting must never be despawned or respawned when the night
/// ends (05:00); the only way they leave the fighter's view is by dying.
/// </summary>
public sealed class DawnCombatGuardTests
{
    private const ulong ScoutConnection = 77UL;
    private const ulong FighterConnection = 88UL;

    // Spawn rolls use the gameplay secret, not the world seed (ADR-026): this fixed secret pins a frontier monster a
    // level-1 fighter can fight through dawn, as the world seed did before.
    private const ulong DawnFightSecret = 7UL;

    [Fact]
    public async Task StepFixedTick_FightingAMonsterThroughDawn_MonsterLeavesOnlyWhenKilled()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-dawn-combat-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
            AuthoritativeWorld world = new(
                1337UL,
                32f,
                0.0008f,
                30,
                new SqliteChunkDeltaRepository(factory),
                new SqliteCharacterStore(factory),
                sink,
                entropy: TestGameplayEntropy.Create(DawnFightSecret));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);
            _ = world.ApplyDebugOverrides(new WorldDebugOverrides { StartHour = 4.95f });

            // A scout in the frontier (seed 1337, chunk 6) finds a monster; a fighter then enters right next to it.
            Vector2 frontier = new(((6 * 32) + 16) * 32f, 16f * 32f);
            CharacterRecord scout = new(Guid.NewGuid(), Guid.NewGuid(), "Scout", 1, 0, frontier.X, frontier.Y, 100f, 100f, 0, [], DateTimeOffset.UtcNow);
            world.TryEnterWorld(ScoutConnection, scout).Status.Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            EntitySpawnSnapshot mob = sink.Spawns.Find(s => s.ConnectionId == ScoutConnection && s.Snapshot.Kind == EntityKind.Monster).Snapshot;
            mob.NetworkId.Should().NotBe(0UL, "the frontier populates monsters on the first scan");

            CharacterRecord fighter = scout with { Id = Guid.NewGuid(), Name = "Fighter", PositionX = mob.PositionX + 16f, PositionY = mob.PositionY };
            world.TryEnterWorld(FighterConnection, fighter).Status.Should().Be(OpStatus.Ok);
            Hour(world).Should().BeLessThan(5f, "the fight starts at night");

            bool killed = false;
            for (int t = 0; t < 900 && !killed; t++)
            {
                // The fighter keeps after the monster when a hit knocks it out of reach, so the fight does not hang
                // on where the generated terrain happens to stop the knockback.
                Vector2 aim = sink.LastPositions.TryGetValue(mob.NetworkId, out Vector2 p) ? p : new Vector2(mob.PositionX, mob.PositionY);
                Vector2 toMob = sink.SelfPositions.TryGetValue(FighterConnection, out Vector2 self) ? aim - self : Vector2.Zero;
                if (toMob.LengthSquared() > 40f * 40f)
                {
                    world.EnqueueMoveInput(FighterConnection, (uint)t + 1U, Vector2.Normalize(toMob), sprint: false);
                }
                else if (t % 10 == 0)
                {
                    _ = world.EnqueueAttackInput(FighterConnection, mob.NetworkId, aim, 2);
                }

                world.StepFixedTick();
                killed = sink.CombatHits.Exists(h => h.DefenderNetworkId == mob.NetworkId && h.IsKilled);
                if (!killed)
                {
                    sink.Despawns.Should().NotContain(
                        (FighterConnection, mob.NetworkId),
                        $"the monster being fought must not vanish at {Hour(world):0.00}h without being killed");
                }
            }

            killed.Should().BeTrue("the fighter kills the monster");
            Hour(world).Should().BeGreaterThan(5f, "the fight lasted past dawn");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    private static float Hour(AuthoritativeWorld world) => WorldProgression.ComputeTimeOfDayHours(world.CurrentTick, world.TickRate);
}

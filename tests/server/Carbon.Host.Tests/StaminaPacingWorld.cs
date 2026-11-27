// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using FluentAssertions;
using NSubstitute;

namespace Carbon.Host.Tests;

/// <summary>
/// A real <see cref="AuthoritativeWorld"/> on the SHIPPED stamina, progression, item and monster config (ADR-033
/// amendment): a hero of a given level with typical attribute points and gear plays the activities the owner felt in
/// game. It drives the same inputs a client sends (move, attack, harvest key) and reads only what the server sends back,
/// so the numbers it reports are the real experience, not a formula.
/// </summary>
internal sealed class StaminaPacingWorld
{
    public const int TicksPerSecond = 30;
    public const ushort WildSlimeArchetype = 101;

    private const ulong Seed = 1337UL;
    private const ulong HeroConnection = 4100UL;
    private const float TileSize = 32f;
    private const byte SwordSlot = 0;
    private const byte AxeSlot = 1;

    // A sword wears fast on stone (ToolUsagePolicy: blade blunt on stone), so the boulder runs use the same sword from
    // spare hotbar slots as each one breaks: every swing is still a real swing of that sword at that cost.
    private const byte FirstSpareSwordSlot = 2;
    private const byte LastSpareSwordSlot = 8;

    private readonly CharacterRecord _hero;
    private uint _clientTick;
    private byte _swordSlot;
    private (int X, int Y)? _boulder;

    private StaminaPacingWorld(AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink, CharacterRecord hero, ulong heroNetworkId)
    {
        this.World = world;
        this.Sink = sink;
        _hero = hero;
        this.HeroNetworkId = heroNetworkId;
    }

    public AuthoritativeWorld World { get; }

    public AuthoritativeWorldMultiplayerTests.RecordingOutboundSink Sink { get; }

    public ulong HeroNetworkId { get; }

    /// <summary>Gets a value indicating whether the server last reported the hero exhausted.</summary>
    public bool IsExhausted => this.Sink.StaminaStatuses.LastOrDefault(s => s.ConnectionId == HeroConnection).Exhausted;

    /// <summary>Gets the server's running count of refused stamina actions (<c>StaminaStatusSync</c>).</summary>
    public uint Refusals => this.Sink.StaminaStatuses.LastOrDefault(s => s.ConnectionId == HeroConnection).RefusalCount;

    /// <summary>
    /// Gets how many times the hero had to stop since status <paramref name="from"/>: the body became exhausted, or an
    /// action was refused (a refusal above the recover fraction clears within the tick, ADR-033 D4, but still stops the
    /// swing). A refusal while already exhausted is the same stop.
    /// </summary>
    /// <param name="from">Index into the recorded statuses.</param>
    /// <returns>Stops.</returns>
    public int StopsSince(int from)
    {
        int count = 0;
        (ulong _, bool Exhausted, uint RefusalCount) last = this.Sink.StaminaStatuses.Take(from).LastOrDefault(s => s.ConnectionId == HeroConnection);
        foreach ((ulong connectionId, bool exhausted, uint refusals) in this.Sink.StaminaStatuses.Skip(from))
        {
            if (connectionId != HeroConnection)
            {
                continue;
            }

            if (!last.Exhausted && (exhausted || refusals > last.RefusalCount))
            {
                count++;
            }

            last = (connectionId, exhausted, refusals);
        }

        return count;
    }

    /// <summary>Creates the world with a hero of <paramref name="build"/> standing next to the first oak near spawn.</summary>
    /// <param name="build">Hero.</param>
    /// <param name="stamina">Starting stamina (default: full).</param>
    public static StaminaPacingWorld Create(HeroBuild build, float? stamina = null)
    {
        (AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink) = NewWorld();
        return Enter(world, sink, build, StandingPoint(world, IsOak, out _), stamina);
    }

    /// <summary>
    /// Creates the world with a hero of <paramref name="build"/> standing next to the first boulder near spawn: a target a
    /// sword swing reaches (so it pays, ADR-033 amendment) but cannot damage, so it never runs out.
    /// </summary>
    public static StaminaPacingWorld CreateAtBoulder(HeroBuild build)
    {
        (AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink) = NewWorld();
        return Enter(world, sink, build, StandingPoint(world, IsBoulder, out _));
    }

    /// <summary>
    /// Creates the world, walks scouts out from spawn (monster level grows with the distance, <c>spawning.json</c>) until
    /// one sees a monster of <paramref name="archetype"/> (0: any non-elite) at <paramref name="level"/>, and puts the hero
    /// next to it in that same world.
    /// </summary>
    public static StaminaPacingWorld CreateNextToMonster(HeroBuild build, ushort archetype, int level, out EntitySpawnSnapshot monster, float startDistance = 0f)
    {
        (AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink) = NewWorld();
        Vector2 spawn = world.DefaultSpawnWorldPosition;
        ulong probe = 9000UL;
        for (int step = 1; step <= 120; step++)
        {
            foreach (Vector2 dir in new[] { Vector2.UnitX, Vector2.UnitY, -Vector2.UnitX, -Vector2.UnitY })
            {
                probe++;
                Vector2 at = spawn + (dir * (startDistance + (step * 512f)));
                CharacterRecord scout = new(Guid.NewGuid(), Guid.NewGuid(), "Scout", 1, 0, at.X, at.Y, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
                world.TryEnterWorld(probe, scout).Status.Should().Be(OpStatus.Ok);
                world.StepFixedTick();
                EntitySpawnSnapshot? mob = sink.Spawns
                    .Where(s => s.ConnectionId == probe && s.Snapshot.Kind == EntityKind.Monster && s.Snapshot.Level == level &&
                        (archetype == 0 ? !s.Snapshot.Name.Contains("King", StringComparison.Ordinal) : s.Snapshot.ArchetypeId == archetype))
                    .Select(s => (EntitySpawnSnapshot?)s.Snapshot)
                    .FirstOrDefault();
                if (mob is not null)
                {
                    monster = mob.Value;
                    StaminaPacingWorld pacing = Enter(world, sink, build, new Vector2(monster.PositionX + 30f, monster.PositionY));
                    _ = world.LeaveWorld(probe);
                    return pacing;
                }

                _ = world.LeaveWorld(probe);
            }
        }

        throw new InvalidOperationException($"No level-{level} monster {archetype} found for seed {Seed}.");
    }

    public float Stamina()
    {
        this.World.TryGetPendingCharacterSnapshot(_hero.Id, out CharacterRecord? snapshot).Should().BeTrue();
        return snapshot!.CurrentStamina;
    }

    public float MaxStamina()
    {
        this.World.TryGetPendingCharacterSnapshot(_hero.Id, out CharacterRecord? snapshot).Should().BeTrue();
        return snapshot!.MaxStamina;
    }

    /// <summary>Sprints back and forth from the current stamina until the body is exhausted; returns the seconds.</summary>
    public float SprintUntilExhausted()
    {
        int ticks = 0;
        while (!this.IsExhausted && ticks < 120 * TicksPerSecond)
        {
            this.World.EnqueueMoveInput(HeroConnection, ++_clientTick, new Vector2(ticks % 60 < 30 ? 1f : -1f, 0f), sprint: true);
            this.World.StepFixedTick();
            ticks++;
        }

        return ticks / (float)TicksPerSecond;
    }

    /// <summary>Swings the sword at the air as fast as the cooldown allows until a swing is refused; returns paid swings.</summary>
    public int SwingUntilRefused()
    {
        float before = this.Stamina();
        int paid = 0;
        for (int t = 0; t < 120 * TicksPerSecond && !this.IsExhausted; t++)
        {
            this.SwingAtBoulder();
            this.World.StepFixedTick();
            float now = this.Stamina();
            if (now < before - 0.01f)
            {
                paid++;
            }

            before = now;
        }

        return paid;
    }

    /// <summary>
    /// Swings the sword for <paramref name="seconds"/> like a player holding the button, skipping swings while exhausted
    /// (the client refuses them locally, ADR-033 D7). Returns the paid swings and the rests (exhaustions).
    /// </summary>
    public (int Swings, int Rests) SwingFor(int seconds)
    {
        int from = this.Sink.StaminaStatuses.Count;
        float before = this.Stamina();
        int paid = 0;
        for (int t = 0; t < seconds * TicksPerSecond; t++)
        {
            if (!this.IsExhausted)
            {
                this.SwingAtBoulder();
            }

            this.World.StepFixedTick();
            float now = this.Stamina();
            if (now < before - 0.01f)
            {
                paid++;
            }

            before = now;
        }

        return (paid, this.StopsSince(from));
    }

    /// <summary>Stands still from the current (exhausted) state; returns seconds until actions unlock and until full.</summary>
    public (float Unlocked, float Full) RestFromExhaustion()
    {
        int ticks = 0;
        float unlocked = -1f;
        float max = this.MaxStamina();
        while (ticks < 120 * TicksPerSecond && this.Stamina() < max - 0.01f)
        {
            this.World.StepFixedTick();
            ticks++;
            if (unlocked < 0f && !this.IsExhausted)
            {
                unlocked = ticks / (float)TicksPerSecond;
            }
        }

        return (unlocked, ticks / (float)TicksPerSecond);
    }

    /// <summary>
    /// Fights <paramref name="mob"/> with the sword: closes in and attacks whenever the cooldown allows, like a player
    /// holding the attack button. Returns the outcome.
    /// </summary>
    public FightResult Fight(EntitySpawnSnapshot mob, int maxSeconds = 60)
    {
        float minStamina = this.Stamina();
        int ticks = 0;
        bool killed = false;
        int startRefusals = this.Sink.StaminaStatuses.Count;
        for (; ticks < maxSeconds * TicksPerSecond && !killed; ticks++)
        {
            Vector2 aim = this.Sink.LastPositions.TryGetValue(mob.NetworkId, out Vector2 m) ? m : new Vector2(mob.PositionX, mob.PositionY);
            Vector2 self = this.Sink.SelfPositions.TryGetValue(HeroConnection, out Vector2 s) ? s : new Vector2(_hero.PositionX, _hero.PositionY);
            Vector2 toMob = aim - self;
            if (toMob.LengthSquared() > 36f * 36f)
            {
                this.World.EnqueueMoveInput(HeroConnection, ++_clientTick, Vector2.Normalize(toMob), sprint: false);
            }
            else
            {
                this.World.EnqueueMoveInput(HeroConnection, ++_clientTick, Vector2.Zero, sprint: false);
                _ = this.World.EnqueueAttackInput(HeroConnection, mob.NetworkId, aim, SwordSlot);
            }

            this.World.StepFixedTick();
            minStamina = MathF.Min(minStamina, this.Stamina());
            killed = this.Sink.CombatHits.Exists(h => h.DefenderNetworkId == mob.NetworkId && h.IsKilled);
        }

        int exhausted = this.StopsSince(startRefusals);
        int hits = this.Sink.CombatHits.Count(h => h.AttackerNetworkId == this.HeroNetworkId && h.DefenderNetworkId == mob.NetworkId);
        return new FightResult(killed, ticks / (float)TicksPerSecond, hits, minStamina, exhausted);
    }

    /// <summary>Chops the oak next to the hero with the axe, pressing the harvest key every tick, until it falls.</summary>
    public ChopResult ChopOak(int maxSeconds = 120)
    {
        Vector2 at = StandingPoint(this.World, IsOak, out (int X, int Y) oak);
        Vector2.Distance(at, new Vector2(_hero.PositionX, _hero.PositionY)).Should().BeLessThan(1f, "the hero was created next to the oak");
        float minStamina = this.Stamina();
        int startStatuses = this.Sink.StaminaStatuses.Count;
        int ticks = 0;
        bool felled = false;
        int swings = 0;
        for (; ticks < maxSeconds * TicksPerSecond && !felled; ticks++)
        {
            float before = this.Stamina();
            if (!this.IsExhausted)
            {
                // A client does not send the swing while exhausted (ADR-033 D7).
                this.SelectSlot(AxeSlot);
                _ = this.World.TryHarvestProp(HeroConnection, oak.X, oak.Y);
            }

            this.World.StepFixedTick();
            if (this.Stamina() < before - 0.01f)
            {
                swings++;
            }

            minStamina = MathF.Min(minStamina, this.Stamina());
            felled = this.Sink.PropChanges.Exists(p => p.TileX == oak.X && p.TileY == oak.Y && p.Durability <= 0f);
        }

        int rests = this.StopsSince(startStatuses);
        return new ChopResult(felled, ticks / (float)TicksPerSecond, swings, minStamina, rests);
    }

    /// <summary>Swings the sword at the air until stamina is below <paramref name="target"/>.</summary>
    public void SwingDownTo(float target)
    {
        for (int t = 0; t < 60 * TicksPerSecond && this.Stamina() >= target && !this.IsExhausted; t++)
        {
            this.SwingAtBoulder();
            this.World.StepFixedTick();
        }
    }

    public void Idle(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            this.World.StepFixedTick();
        }
    }

    /// <summary>Tries the axe on the oak once (harvest key) and returns the server's answer.</summary>
    public OpStatus SwingAxeAtOak()
    {
        _ = StandingPoint(this.World, IsOak, out (int X, int Y) oak);
        this.SelectSlot(AxeSlot);
        return this.World.TryHarvestProp(HeroConnection, oak.X, oak.Y).Status;
    }

    public bool OakDamaged()
    {
        _ = StandingPoint(this.World, IsOak, out (int X, int Y) oak);
        return this.Sink.PropChanges.Exists(p => p.TileX == oak.X && p.TileY == oak.Y);
    }

    /// <summary>Queues a sword swing at the boulder the hero stands next to (a paid, connecting swing).</summary>
    public void SwingAtBoulder()
    {
        if (_boulder is null)
        {
            _ = StandingPoint(this.World, IsBoulder, out (int X, int Y) found);
            _boulder = found;
        }

        (int bx, int by) = _boulder.Value;
        _ = this.World.EnqueueAttackInput(HeroConnection, 0UL, new Vector2((bx + 0.5f) * TileSize, (by + 0.5f) * TileSize), this.CurrentSword());
    }

    /// <summary>Queues a sword swing into the open air east of the hero, away from the prop it stands next to.</summary>
    public void Whiff()
    {
        Vector2 self = this.Sink.SelfPositions.TryGetValue(HeroConnection, out Vector2 p) ? p : new Vector2(_hero.PositionX, _hero.PositionY);
        _ = this.World.EnqueueAttackInput(HeroConnection, 0UL, self + new Vector2(200f, 0f), this.CurrentSword());
    }

    /// <summary>The hotbar slot of a sword that has not broken yet.</summary>
    private byte CurrentSword()
    {
        while (_swordSlot < LastSpareSwordSlot &&
            this.Sink.SlotChanges.Exists(c => c.ConnectionId == HeroConnection && c.SlotIndex == _swordSlot && c.Quantity == 0))
        {
            _swordSlot = _swordSlot == SwordSlot ? FirstSpareSwordSlot : (byte)(_swordSlot + 1);
        }

        return _swordSlot;
    }

    private void SelectSlot(byte slot) => _ = this.World.TrySelectHotbarSlot(HeroConnection, slot);

    private static (AuthoritativeWorld World, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink Sink) NewWorld()
    {
        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        AuthoritativeWorld world = new(
            Seed,
            TileSize,
            0.0008f,
            TicksPerSecond,
            Substitute.For<IChunkDeltaRepository>(),
            Substitute.For<ICharacterStore>(),
            sink,
            entropy: TestGameplayEntropy.Create(Seed));
        return (world, sink);
    }

    private static StaminaPacingWorld Enter(AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink, HeroBuild build, Vector2 at, float? stamina = null)
    {
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        inv.SetSlot(SwordSlot, new ItemSlot(build.SwordId, 1, 0, 500f));
        inv.SetSlot(AxeSlot, new ItemSlot(build.AxeId, 1, 0, 500f));
        for (byte spare = FirstSpareSwordSlot; spare <= LastSpareSwordSlot; spare++)
        {
            inv.SetSlot(spare, new ItemSlot(build.SwordId, 1, 0, 500f));
        }
        CharacterProgressionState progression = new();
        progression.SetAttribute(PrimaryAttributeId.Power, build.PowerPoints);
        progression.SetAttribute(PrimaryAttributeId.Agility, build.AgilityPoints);
        float maxStamina = world.Progression.ComputeStats(build.Level, progression, default).MaxStamina;

        // Plenty of health: these runs measure stamina pacing, so the monster must not end a fight by killing the hero.
        CharacterRecord hero = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Pacer",
            build.Level,
            AppearancePreset: 0,
            PositionX: at.X,
            PositionY: at.Y,
            CurrentHealth: 5000f,
            MaxHealth: 5000f,
            SelectedHotbarSlot: SwordSlot,
            InventoryBlob: InventoryBinaryCodec.Serialize(inv),
            UpdatedAtUtc: DateTimeOffset.UnixEpoch,
            CurrentStamina: stamina ?? maxStamina,
            MaxStamina: maxStamina,
            ProgressionBlob: CharacterProgressionBinaryCodec.Serialize(progression));
        EnterWorldResult entered = world.TryEnterWorld(HeroConnection, hero);
        entered.Status.Should().Be(OpStatus.Ok);
        return new StaminaPacingWorld(world, sink, hero, entered.PlayerNetworkId);
    }

    private static bool IsOak(in MapProp prop) => prop.Type == MapPropType.OakTree;

    private static bool IsBoulder(in MapProp prop) => prop.Type == MapPropType.Boulder;

    /// <summary>The first matching prop near spawn (deterministic generator) and a point one tile east of it.</summary>
    private static Vector2 StandingPoint(AuthoritativeWorld world, PropFilter filter, out (int X, int Y) oak)
    {
        ChunkWorld2D mirror = new(new ProceduralWorldGenerator(Seed), TileSize, 0.0008f);
        int sx = (int)MathF.Floor(world.DefaultSpawnWorldPosition.X / TileSize);
        int sy = (int)MathF.Floor(world.DefaultSpawnWorldPosition.Y / TileSize);
        for (int r = 0; r <= 96; r++)
        {
            foreach ((int dx, int dy) in Ring(r))
            {
                if (mirror.TryGetPropAt(sx + dx, sy + dy, out MapProp prop) && filter(in prop) && !prop.Destructibility.IsDestroyed)
                {
                    oak = (sx + dx, sy + dy);
                    return new Vector2(((oak.X + 0.5f) * TileSize) + 40f, (oak.Y + 0.5f) * TileSize);
                }
            }
        }

        throw new InvalidOperationException("No matching prop near spawn for the test seed.");
    }

    private static IEnumerable<(int Dx, int Dy)> Ring(int r) =>
        Enumerable.Range(-r, (2 * r) + 1)
            .SelectMany(dy => Enumerable.Range(-r, (2 * r) + 1).Select(dx => (dx, dy)))
            .Where(p => Math.Max(Math.Abs(p.dx), Math.Abs(p.dy)) == r);
}

/// <summary>A hero level with typical attribute points and gear (ADR-033 amendment).</summary>
/// <param name="Level">Character level.</param>
/// <param name="PowerPoints">Points in power (+2 max stamina each).</param>
/// <param name="AgilityPoints">Points in agility (+6 max stamina each).</param>
/// <param name="SwordId">Held sword.</param>
/// <param name="AxeId">Held axe.</param>
internal sealed record HeroBuild(ushort Level, ushort PowerPoints, ushort AgilityPoints, uint SwordId, uint AxeId);

internal readonly record struct FightResult(bool Killed, float Seconds, int Hits, float MinStamina, int Exhaustions);

internal readonly record struct ChopResult(bool Felled, float Seconds, int Swings, float MinStamina, int Rests);

internal delegate bool PropFilter(in MapProp prop);

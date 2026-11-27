// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World.Construction;
using Carbon.Application.World.Loot;
using Carbon.Application.World.Players;
using Carbon.Application.World.Props;
using Carbon.Application.World.Replication;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class PropHarvestServiceTests
{
    private const float TileSize = 32f;
    private const ulong Seed = 1337UL;

    [Fact]
    public void Strike_UntilThePropBreaks_MarksTheChunkDirtyDropsItsHarvestAndGrantsExperience()
    {
        Harness h = new();
        (int tx, int ty, MapProp prop) = h.FindDestructibleProp();
        ItemSlot strongestTool = h.StrongestToolFor(prop);

        PropStrikeResult last = default;
        for (int i = 0; i < 200 && !last.Prop.Destructibility.IsDestroyed; i++)
        {
            _ = h.Chunks.TryGetPropAt(tx, ty, out MapProp current);
            last = h.Props.Strike(h.Player, tx, ty, in current, in strongestTool, replicateToolWear: false, out _);
            last.Damaged.Should().BeTrue("the strongest tool always damages a destructible prop");
        }

        last.Prop.Destructibility.IsDestroyed.Should().BeTrue();
        last.Revision.Should().BeGreaterThan(0U);
        h.DirtyChunks.Should().NotBeEmpty();
        h.ExperienceGrants.Should().ContainSingle().Which.Should().BeGreaterThan(0U);
    }

    [Fact]
    public void Strike_WithBareHandsOnAToughProp_IsBlockedAndChangesNothing()
    {
        Harness h = new();
        (int tx, int ty, MapProp prop) = h.FindDestructibleProp(minimumTier: 2);

        PropStrikeResult strike = h.Props.Strike(h.Player, tx, ty, in prop, ItemSlot.Empty, replicateToolWear: false, out ItemSlot? wear);

        strike.Damaged.Should().BeFalse();
        strike.Prop.Should().Be(prop);
        wear.Should().BeNull("bare hands do not wear");
        h.DirtyChunks.Should().BeEmpty();
        h.ExperienceGrants.Should().BeEmpty();
    }

    [Fact]
    public void Strike_ReplicatingToolWear_QueuesTheWornSlot()
    {
        Harness h = new() { WearReturns = true };
        (int tx, int ty, MapProp prop) = h.FindDestructibleProp();
        ItemSlot tool = h.StrongestToolFor(prop);

        _ = h.Props.Strike(h.Player, tx, ty, in prop, in tool, replicateToolWear: true, out ItemSlot? wear);

        wear.Should().NotBeNull();
        h.Outbound.Pending.SlotChanges.Should().ContainSingle();
    }

    [Theory]
    [InlineData(900UL, true)]
    [InlineData(0UL, false)]
    public void Strike_BreakingTheProp_SchedulesItsConfiguredRegrowth(ulong delayTicks, bool comesBack)
    {
        Harness h = new() { RegrowDelayTicks = delayTicks };
        (int tx, int ty, MapProp prop) = h.FindDestructibleProp();
        ItemSlot tool = h.StrongestToolFor(prop);

        PropStrikeResult last = default;
        for (int i = 0; i < 200 && !last.Prop.Destructibility.IsDestroyed; i++)
        {
            _ = h.Chunks.TryGetPropAt(tx, ty, out MapProp current);
            last = h.Props.Strike(h.Player, tx, ty, in current, in tool, replicateToolWear: false, out _);
        }

        last.Prop.Destructibility.IsDestroyed.Should().BeTrue();
        h.Chunks.TryGetDelta(Chunk2D.ToChunkCoord(tx), Chunk2D.ToChunkCoord(ty), out ChunkDelta? delta).Should().BeTrue();
        bool scheduled = delta!.TryGetRespawnTick(Chunk2D.ToLocalCoord(tx), Chunk2D.ToLocalCoord(ty), out ulong at);
        scheduled.Should().Be(comesBack, "the regrowth delay comes from props.json, 0 meaning never");
        if (comesBack)
        {
            at.Should().Be(100UL + delayTicks, "the prop comes back the configured ticks after it broke");
        }
    }

    [Fact]
    public void IsWithinHarvestRange_UsesTheTileCenterAndTheSwingReach()
    {
        Harness h = new();

        h.Props.IsWithinHarvestRange(new Vector2(16f, 16f), 1, 0).Should().BeTrue("32px between centers");
        h.Props.IsWithinHarvestRange(new Vector2(16f, 16f), 2, 0).Should().BeFalse("64px is past the 52px reach");
    }

    private sealed class Harness
    {
        private ulong _nextNetworkId = 8000UL;

        public Harness()
        {
            GroundLootService loot = new(this.Simulation, () => this.Rules, () => ++_nextNetworkId, () => 100UL, (_, _) => { });
            ConstructionService construction = new(
                this.Chunks,
                StructureCatalog.LoadEmbedded(),
                (_, _) => false,
                this.Simulation,
                TileSize,
                () => this.Rules,
                this.Outbound,
                this.DirtyChunks,
                (_, _) => (0, 1),
                (_, _) => { });
            this.Props = new PropHarvestService(
                this.Simulation,
                this.Chunks,
                TileSize,
                (_, _, _) => this.RegrowDelayTicks,
                () => this.Rules,
                () => 100UL,
                loot,
                construction,
                this.Outbound,
                this.DirtyChunks,
                (_, _) => (0, 1),
                (_, _, _) => true,
                this.Wear,
                (_, xp) => this.ExperienceGrants.Add(xp));

            ConnectedPlayerSession session = new(9UL, Guid.NewGuid(), Guid.NewGuid(), "Tester", 900UL, 0, 0);
            CharacterRecord character = new(
                session.CharacterId, session.AccountId, "Tester", 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
            this.Player = new PlayerState(session, this.Simulation.SpawnNetworkEntity(900UL, Vector2.Zero, 7f), character, new InventoryContainer(4));
        }

        public ItemRules Rules { get; } = ItemRules.LoadShippedDefaults();

        /// <summary>Gets or sets the regrowth delay every destroyed prop is scheduled with (0 = never comes back).</summary>
        public ulong RegrowDelayTicks { get; set; } = 900UL;

        public WorldSimulation Simulation { get; } = new();

        public ChunkWorld2D Chunks { get; } = new(new ProceduralWorldGenerator(Seed), TileSize, 0.0008f);

        public OutboundTickBuffer Outbound { get; } = new();

        public HashSet<long> DirtyChunks { get; } = [];

        public List<uint> ExperienceGrants { get; } = [];

        public PropHarvestService Props { get; }

        public PlayerState Player { get; }

        public bool WearReturns { get; init; }

        public (int TileX, int TileY, MapProp Prop) FindDestructibleProp(byte minimumTier = 0)
        {
            for (int r = 0; r <= 96; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r &&
                            this.Chunks.TryGetPropAt(dx, dy, out MapProp prop) &&
                            prop.Destructibility.IsDestructible &&
                            !prop.Destructibility.IsDestroyed &&
                            prop.Destructibility.HarvestTier >= minimumTier)
                        {
                            return (dx, dy, prop);
                        }
                    }
                }
            }

            throw new InvalidOperationException("The seeded world has no matching prop near the origin.");
        }

        public ItemSlot StrongestToolFor(MapProp prop)
        {
            ItemSlot best = ItemSlot.Empty;
            float bestDamage = 0f;
            ToolTargetKind kind = prop.Category == PropCategory.Mineral ? ToolTargetKind.Mineral : ToolTargetKind.Flora;
            foreach (ItemArchetype item in this.Rules.Items.All)
            {
                ItemSlot candidate = new(item.Id, 1, Durability: item.MaxDurability);
                PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(this.Rules, in candidate, kind, prop.Destructibility.HarvestTier);
                if (eval.Outcome == ToolStrikeOutcome.Optimal && eval.Damage > bestDamage)
                {
                    best = candidate;
                    bestDamage = eval.Damage;
                }
            }

            return best.IsEmpty ? throw new InvalidOperationException("No shipped tool fits the prop.") : best;
        }

        private bool Wear(PlayerState state, in ItemSlot activeSlot, float loss, out ItemSlot updatedSlot)
        {
            updatedSlot = activeSlot with { Durability = activeSlot.Durability - loss };
            return this.WearReturns;
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Building;
using Carbon.Maps.Props;
using Carbon.Primitives.Random;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-040 D1 and D1b through the authoritative world: a felled tree becomes a stump and grows back stage by stage on the
/// configured times; each stage yields its configured wood; a mature tree drops saplings on fixed entropy; a stump can be
/// dug up for good (by hand, slowly), which clears the tile for building; and growth waits while a player is near.
/// </summary>
public sealed class TreeGrowthWorldTests
{
    private const int TickRate = RegrowthWorldFixture.TickRate;

    [Fact]
    public async Task TryHarvestProp_MatureTreeFelled_LeavesAStumpThatGrowsBackOnTheStageTimes()
    {
        (int x, int y) = FindTree();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, ItemSlot.Empty).ConfigureAwait(true);

        _ = f.StrikeUntil(x, y, p => p.Stage == TreeGrowthStage.Stump);
        f.Probe(x, y).Should().Be((false, TreeGrowthStage.Stump), "a felled tree leaves a standing stump, not an empty tile");

        f.Step(5 * TickRate);
        f.Probe(x, y).Stage.Should().Be(TreeGrowthStage.Stump, "the stump stage lasts 6 s");
        f.Step(2 * TickRate);
        f.Probe(x, y).Stage.Should().Be(TreeGrowthStage.Sapling);
        f.Step(4 * TickRate);
        f.Probe(x, y).Stage.Should().Be(TreeGrowthStage.Young);
        f.Step(18 * TickRate);
        f.Probe(x, y).Stage.Should().Be(TreeGrowthStage.Young, "the young stage lasts 20 s");
        f.Step(2 * TickRate);
        f.Probe(x, y).Should().Be((false, TreeGrowthStage.Mature), "after stump, sapling and young the tree is full grown again");
    }

    [Fact]
    public async Task TryHarvestProp_EachStageFelled_DropsItsConfiguredWood()
    {
        (int x, int y) = FindTree();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, ItemSlot.Empty).ConfigureAwait(true);
        TreeGrowthRules trees = f.World.PropRegrowth.Trees;

        _ = f.StrikeUntil(x, y, p => p.Stage == TreeGrowthStage.Stump);
        f.DroppedQuantity(StarterGameCatalog.WoodLog).Should().Be(trees.MatureYield, "a mature tree drops trees.mature.yield logs");

        f.ClearDrops();
        f.Step(11 * TickRate);
        f.Probe(x, y).Stage.Should().Be(TreeGrowthStage.Young);
        _ = f.StrikeUntil(x, y, p => p.Destroyed);
        f.DroppedQuantity(StarterGameCatalog.WoodLog).Should().Be(trees.Young.Yield, "a young tree drops fewer logs");
        f.DroppedQuantity(SaplingId(f.World)).Should().Be(0, "a felled young tree drops no sapling");
    }

    [Fact]
    public async Task TryHarvestProp_YoungTreeFelled_LeavesNoStumpAndTheTileStaysClear()
    {
        (int x, int y) = FindTree();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, ItemSlot.Empty).ConfigureAwait(true);
        _ = f.StrikeUntil(x, y, p => p.Stage == TreeGrowthStage.Stump);
        f.Step(11 * TickRate);
        f.Probe(x, y).Stage.Should().Be(TreeGrowthStage.Young);

        _ = f.StrikeUntil(x, y, p => p.Destroyed || p.Stage == TreeGrowthStage.Stump);

        f.Probe(x, y).Should().Be((true, TreeGrowthStage.Young), "only a mature tree leaves a stump (ADR-040 D1 amendment)");
        f.Step(60 * TickRate);
        f.Probe(x, y).Destroyed.Should().BeTrue("a felled young tree never regrows");
        f.World.TryPlaceStructure(RegrowthWorldFixture.Conn, x, y, StructureType.WoodFloor).Status.Should().Be(OpStatus.Ok, "the tile is free");
    }

    [Fact]
    public async Task TryHarvestProp_SaplingCut_IsUprootedBackIntoItsItemAndTheTileStaysClear()
    {
        (int x, int y) = FindTree();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, ItemSlot.Empty).ConfigureAwait(true);
        _ = f.StrikeUntil(x, y, p => p.Stage == TreeGrowthStage.Stump);
        f.Step(7 * TickRate);
        f.Probe(x, y).Stage.Should().Be(TreeGrowthStage.Sapling);
        f.ClearDrops();

        _ = f.StrikeUntil(x, y, p => p.Destroyed || p.Stage == TreeGrowthStage.Stump);

        f.Probe(x, y).Should().Be((true, TreeGrowthStage.Sapling), "a cut sapling does not turn into a stump");
        f.DroppedQuantity(SaplingId(f.World)).Should().Be(1, "the uprooted sapling comes back as its item");
        f.DroppedQuantity(StarterGameCatalog.WoodLog).Should().Be(0);
        f.Step(60 * TickRate);
        f.Probe(x, y).Destroyed.Should().BeTrue("nothing regrows on the tile");
        f.World.TryPlaceStructure(RegrowthWorldFixture.Conn, x, y, StructureType.WoodFloor).Status.Should().Be(OpStatus.Ok, "the tile is free");
    }

    [Fact]
    public async Task TryHarvestProp_MatureAndStumpSaplingChances_AreEachTheirOwn()
    {
        (int x, int y) = FindTree();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(
            RegrowthWorldFixture.Rules(matureSaplingChance: 1f, stumpSaplingChance: 0f), x, y, ItemSlot.Empty).ConfigureAwait(true);

        _ = f.StrikeUntil(x, y, p => p.Stage == TreeGrowthStage.Stump);
        f.DroppedQuantity(SaplingId(f.World)).Should().Be(1, "trees.mature.saplingChance 1 always drops one");
        f.ClearDrops();
        _ = f.StrikeUntil(x, y, p => p.Destroyed);
        f.DroppedQuantity(SaplingId(f.World)).Should().Be(0, "trees.stump.saplingChance 0 never drops one");
    }

    [Fact]
    public async Task TryHarvestProp_MatureTreeFelled_DropsTheSaplingsItsFixedEntropyRolls()
    {
        (int x, int y) = FindTree();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, ItemSlot.Empty).ConfigureAwait(true);
        TreeGrowthRules trees = f.World.PropRegrowth.Trees;

        _ = f.StrikeUntil(x, y, p => p.Stage == TreeGrowthStage.Stump);

        DeterministicRandom roll = TestGameplayEntropy.Create(RegrowthWorldFixture.Seed).KeyAt(f.LastStrikeTick)
            .CreateRandom(GameplayDomain.SaplingDrop, f.LastStrikeTick, TileKey(x, y));
        int expected = roll.NextBool(trees.MatureSaplingChance) ? 1 : 0;
        f.DroppedQuantity(SaplingId(f.World)).Should().Be(expected, "the sapling count is rolled on GameplayEntropy (ADR-026), keyed by tick and tile");
    }

    [Fact]
    public async Task TryHarvestProp_StumpDugUp_DropsWoodAndItsRolledSaplingAndNeverRegrows()
    {
        (int x, int y) = FindTree();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, ItemSlot.Empty).ConfigureAwait(true);
        TreeGrowthRules trees = f.World.PropRegrowth.Trees;
        _ = f.StrikeUntil(x, y, p => p.Stage == TreeGrowthStage.Stump);
        f.ClearDrops();

        int digs = f.StrikeUntil(x, y, p => p.Destroyed);

        digs.Should().Be((int)MathF.Ceiling(trees.Stump.Durability / trees.DigBareHandDamage), "bare hands dig slowly but are never blocked");
        DeterministicRandom roll = TestGameplayEntropy.Create(RegrowthWorldFixture.Seed).KeyAt(f.LastStrikeTick)
            .CreateRandom(GameplayDomain.SaplingDrop, f.LastStrikeTick, TileKey(x, y));
        f.DroppedQuantity(SaplingId(f.World)).Should().Be(roll.NextBool(trees.StumpSaplingChance) ? 1 : 0, "a dug-up stump may drop a sapling");
        f.DroppedQuantity(StarterGameCatalog.WoodLog).Should().Be(trees.Stump.Yield);

        f.Step(60 * TickRate);
        f.Probe(x, y).Should().Be((true, TreeGrowthStage.Stump), "a dug-up stump is gone for good: nothing regrows on the tile");
    }

    [Fact]
    public async Task TryHarvestProp_PickaxeDigsAStumpFasterThanAnAxe_AndWearsIt()
    {
        (int x, int y) = FindTree();
        ItemSlot pickaxe = new(StarterGameCatalog.StonePickaxe, 1, 0, 160f);
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, pickaxe).ConfigureAwait(true);
        _ = f.StrikeUntil(x, y, p => p.Stage == TreeGrowthStage.Stump);
        f.Sink.SlotChanges.Clear();

        int pickaxeDigs = f.StrikeUntil(x, y, p => p.Destroyed);

        pickaxeDigs.Should().BeLessThan((int)MathF.Ceiling(f.World.PropRegrowth.Trees.Stump.Durability / f.World.PropRegrowth.Trees.DigBareHandDamage), "a pickaxe is the digging tool");
        f.Sink.SlotChanges.Should().Contain(c => c.ConnectionId == RegrowthWorldFixture.Conn && c.SlotIndex == 0, "digging wears the tool");
    }

    [Fact]
    public async Task TryPlaceStructure_OnAStumpAndOnTheClearedTile_IsRejectedThenAllowed()
    {
        (int x, int y) = FindTree();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, ItemSlot.Empty).ConfigureAwait(true);
        _ = f.StrikeUntil(x, y, p => p.Stage == TreeGrowthStage.Stump);

        f.World.TryPlaceStructure(RegrowthWorldFixture.Conn, x, y, StructureType.WoodFloor).Status.Should().NotBe(OpStatus.Ok, "a stump blocks building: dig it up first");

        _ = f.StrikeUntil(x, y, p => p.Destroyed);
        f.World.TryPlaceStructure(RegrowthWorldFixture.Conn, x, y, StructureType.WoodFloor).Status.Should().Be(OpStatus.Ok, "a dug-up stump leaves the tile clear for building");
    }

    [Fact]
    public async Task ProcessPropRespawns_PlayerWithinDeferRadius_TheStumpWaitsUntilTheyLeave()
    {
        (int x, int y) = FindTree();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(deferWithinTiles: 6), x, y, ItemSlot.Empty).ConfigureAwait(true);
        _ = f.StrikeUntil(x, y, p => p.Stage == TreeGrowthStage.Stump);

        f.Step(20 * TickRate);

        f.Probe(x, y).Stage.Should().Be(TreeGrowthStage.Stump, "a stage never changes in front of a player within trees.deferWithinTiles");
    }

    private static (int X, int Y) FindTree() =>
        RegrowthWorldFixture.FindProp(p => p.Type == MapPropType.OakTree && p.Destructibility.HarvestTier <= 1, outsideClearing: true);

    private static ulong TileKey(int x, int y) => ((ulong)(uint)x << 32) | (uint)y;

    private static uint SaplingId(AuthoritativeWorld world) => world.ItemRules.ItemCatalog.ResolveId("sapling");
}

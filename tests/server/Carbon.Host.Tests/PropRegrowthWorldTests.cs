// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading.Tasks;
using Carbon.Application.World;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Props;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-040 through the authoritative world: mined stone and ore never come back, spawn forage comes back fast, a pending
/// regrowth survives a server restart, and nothing grows into a player standing on the tile. Tree stages are covered by
/// <see cref="TreeGrowthWorldTests"/>.
/// </summary>
public sealed class PropRegrowthWorldTests
{
    private const int ForageSeconds = 5;
    private const int TickRate = RegrowthWorldFixture.TickRate;

    [Fact]
    public async Task TryHarvestProp_BoulderMined_NeverComesBack()
    {
        (int x, int y) = RegrowthWorldFixture.FindProp(p => p.Type == MapPropType.Boulder, outsideClearing: true);
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(
            RegrowthWorldFixture.Rules(ForageSeconds), x, y, new ItemSlot(StarterGameCatalog.CrystalPickaxe, 1, 0, 500f)).ConfigureAwait(true);

        f.StrikeUntilGone(x, y);
        f.Step(120 * TickRate);

        f.IsDestroyed(x, y).Should().BeTrue("stone and ore never come back (ADR-040 D2)");
    }

    [Fact]
    public async Task TryHarvestProp_StarterForageNearSpawn_ComesBackAfterTheFastForageDelay()
    {
        (int x, int y) = RegrowthWorldFixture.FindProp(p => p.Type == MapPropType.LooseStones, outsideClearing: false);
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(ForageSeconds), x, y, ItemSlot.Empty).ConfigureAwait(true);

        f.StrikeUntilGone(x, y);
        f.IsDestroyed(x, y).Should().BeTrue();
        f.Step((ForageSeconds + 2) * TickRate);

        f.IsDestroyed(x, y).Should().BeFalse("a new player can always gather loose stones at spawn (ADR-040 D3)");
    }

    [Fact]
    public async Task InitializeFromStoreAsync_ServerRestartsWhileForageRegrows_ItStillComesBack()
    {
        (int x, int y) = RegrowthWorldFixture.FindProp(p => p.Type == MapPropType.FallenBranch, outsideClearing: false);
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(forageSeconds: 30), x, y, ItemSlot.Empty).ConfigureAwait(true);
        f.StrikeUntilGone(x, y);
        await f.World.FlushDirtyPersistenceAsync().ConfigureAwait(true);

        AuthoritativeWorld restarted = f.Restart();
        await restarted.InitializeFromStoreAsync().ConfigureAwait(true);
        RegrowthWorldFixture.IsDestroyed(restarted, x, y).Should().BeTrue("the branch is still gone after the restart");
        for (int i = 0; i < 32 * TickRate; i++)
        {
            restarted.StepFixedTick();
        }

        RegrowthWorldFixture.IsDestroyed(restarted, x, y).Should().BeFalse("the respawn timer was saved with the chunk and survived the restart");
    }

    [Fact]
    public async Task InitializeFromStoreAsync_ServerRestartsWhileATreeIsAStump_ItStillGrowsBack()
    {
        (int x, int y) = RegrowthWorldFixture.FindProp(p => p.Type == MapPropType.OakTree, outsideClearing: true);
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, ItemSlot.Empty).ConfigureAwait(true);
        _ = f.StrikeUntil(x, y, p => p.Stage == TreeGrowthStage.Stump);
        await f.World.FlushDirtyPersistenceAsync().ConfigureAwait(true);

        AuthoritativeWorld restarted = f.Restart();
        await restarted.InitializeFromStoreAsync().ConfigureAwait(true);
        RegrowthWorldFixture.Probe(restarted, x, y).Should().Be((false, TreeGrowthStage.Stump), "the stage was saved with the chunk");
        for (int i = 0; i < 7 * TickRate; i++)
        {
            restarted.StepFixedTick();
        }

        RegrowthWorldFixture.Probe(restarted, x, y).Stage.Should().Be(TreeGrowthStage.Sapling, "the next-stage timer survived the restart");
    }

    [Fact]
    public async Task ProcessPropRespawns_PlayerStandsOnTheTile_ForageWaitsForThem()
    {
        (int x, int y) = RegrowthWorldFixture.FindProp(p => p.Type == MapPropType.DryBush, outsideClearing: false);
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(ForageSeconds), x, y, ItemSlot.Empty, standOnTile: true).ConfigureAwait(true);

        f.StrikeUntilGone(x, y);
        f.Step((ForageSeconds + 4) * TickRate);

        f.IsDestroyed(x, y).Should().BeTrue("nothing grows into a player standing on the tile");
    }
}

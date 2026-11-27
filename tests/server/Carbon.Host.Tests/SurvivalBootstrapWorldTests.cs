// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.Identity;
using Carbon.Abstractions.Persistence;
using Carbon.Application.Identity;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Infrastructure.Security;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// A newly created character enters the world with nothing and must reach its first tools with server commands alone: gather
/// loose stones, branches and fiber by hand at spawn, punch trees for more wood, and craft the wood tools by hand.
/// </summary>
public sealed class SurvivalBootstrapWorldTests
{
    private const ulong Seed = 1337UL;
    private const float TileSize = 32f;
    private const ulong Connection = 811UL;

    /// <summary>
    /// The shipped forage delay, with a felled oak's stump grown back to a full tree after 30 s instead of 45 min, so the
    /// axe can be compared with bare hands on the same tree.
    /// </summary>
    private static readonly PropRegrowthRules s_regrowth = PropRegrowthRules.LoadFromJson(
        """
        {
          "spawnForage": { "respawnSeconds": 45 },
          "blockedRetrySeconds": 1,
          "regrowSeconds": { "dryBush": 600 },
          "trees": {
            "deferWithinTiles": 0,
            "stump": { "seconds": 10, "durability": 30, "yield": 1, "saplingChance": 0.5 },
            "sapling": { "seconds": 10, "durability": 10, "yield": 0 },
            "young": { "seconds": 10, "durabilityShare": 0.5, "yield": 1 },
            "mature": { "yield": 3, "saplingChance": 0.2 },
            "dig": { "bareHandDamage": 4, "axeEfficiency": 0.5 }
          }
        }
        """);

    [Fact]
    public async Task EmptyHandedCharacter_GathersAtSpawnCraftsWoodAxe_AndThenChopsATreeFaster()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-bootstrap-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            (AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink, (int X, int Y) spawnTile) =
                await EnterFreshCharacterAsync(factory).ConfigureAwait(true);
            ChunkWorld2D mirror = new(new ProceduralWorldGenerator(Seed), TileSize, 0.0008f);

            // 1. Gather the wood axe's materials by hand from the forage the generator leaves at spawn.
            List<(int X, int Y)> stones = FindProps(mirror, spawnTile, 9, MapPropType.LooseStones);
            List<(int X, int Y)> branches = FindProps(mirror, spawnTile, 9, MapPropType.FallenBranch);
            List<(int X, int Y)> bushes = FindProps(mirror, spawnTile, 9, MapPropType.DryBush);
            foreach ((int X, int Y) tile in stones.Take(2).Concat(branches.Take(3)))
            {
                WalkTo(world, sink, TileCenter(tile));
                StrikeUntilGone(world, tile).Should().Be(1, $"the loose forage at {tile} is picked up with one bare-hand grab");
                PickUpNewLoot(world, sink);
            }

            foreach ((int X, int Y) tile in bushes.Take(2))
            {
                WalkTo(world, sink, TileCenter(tile));
                StrikeUntilGone(world, tile).Should().BePositive($"the dry bush at {tile} is torn up by hand");
                PickUpNewLoot(world, sink);
            }

            Count(world, StarterGameCatalog.StoneChunk).Should().Be(2);
            Count(world, StarterGameCatalog.WoodLog).Should().Be(3);

            // 2. Craft rope and the wood axe without a station.
            world.ItemRules.RecipeCatalog.TryGetByCode("rope", out CraftingRecipe? rope).Should().BeTrue();
            world.ItemRules.RecipeCatalog.TryGetByCode("wood_axe", out CraftingRecipe? axe).Should().BeTrue();
            world.TryCraftRecipe(Connection, (ushort)rope!.Id, 1).Should().Be(OpStatus.Ok, "rope is a hand recipe");
            world.TryCraftRecipe(Connection, (ushort)axe!.Id, 1).Should().Be(OpStatus.Ok, "the wood axe is a hand recipe");
            (ushort axeSlot, ItemSlot _) = Inventory(world).Single(s => s.Slot.ItemId == StarterGameCatalog.WoodAxe);
            Count(world, StarterGameCatalog.WoodLog).Should().Be(0, "the axe used up the gathered logs");

            // 3. A tier-1 tree falls to bare hands, slowly; once it regrows the axe fells it in fewer strikes.
            (int X, int Y) tree = FindProps(mirror, spawnTile, 40, MapPropType.OakTree).First();
            WalkTo(world, sink, TileCenter(tree));
            HashSet<ushort> occupied = [.. Inventory(world).Select(s => s.SlotIndex)];
            byte emptyHand = (byte)Enumerable.Range(0, 8).First(i => !occupied.Contains((ushort)i));
            world.TrySelectHotbarSlot(Connection, emptyHand).Should().Be(OpStatus.Ok);
            int punches = StrikeUntilGone(world, tree, untilStump: true);
            PickUpNewLoot(world, sink);
            Count(world, StarterGameCatalog.WoodLog).Should().BePositive("a punched-down tree drops its logs");

            StepTicks(world, 30 * (30 + 2));
            world.TrySelectHotbarSlot(Connection, (byte)axeSlot).Should().Be(OpStatus.Ok);
            int chops = StrikeUntilGone(world, tree, untilStump: true);

            punches.Should().BeGreaterThan(1, "bare hands fell a tree slowly");
            chops.Should().BePositive("the stump grew back into a full tree that the axe fells again");
            chops.Should().BeLessThan(punches, "the wood axe chops faster than bare hands");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task EmptyHandedCharacter_CraftsEveryWoodToolByHand_AndTheWoodPickaxeMinesABoulderHandsCannot()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-wood-tier-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            (AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink, (int X, int Y) spawnTile) =
                await EnterFreshCharacterAsync(factory).ConfigureAwait(true);
            ChunkWorld2D mirror = new(new ProceduralWorldGenerator(Seed), TileSize, 0.0008f);
            ItemCatalog items = world.ItemRules.ItemCatalog;

            // 1. Spawn forage: every fallen branch and dry bush, by hand.
            foreach ((int X, int Y) tile in FindProps(mirror, spawnTile, 9, MapPropType.FallenBranch).Concat(FindProps(mirror, spawnTile, 9, MapPropType.DryBush)))
            {
                WalkTo(world, sink, TileCenter(tile));
                StrikeUntilGone(world, tile).Should().BePositive($"forage at {tile} comes up by hand");
                PickUpNewLoot(world, sink);
            }

            // 2. Punched wood: two oaks felled with bare hands. Spawn forage regrows fast, so pick it again until there is fiber
            //    for two ropes and the sword's binding.
            foreach ((int X, int Y) tree in FindProps(mirror, spawnTile, 40, MapPropType.OakTree).Take(2))
            {
                WalkTo(world, sink, TileCenter(tree));

                // Bare hands: the saplings a felled oak drops (ADR-040) must not end up in the hand that punches the next one.
                HashSet<ushort> taken = [.. Inventory(world).Select(s => s.SlotIndex)];
                world.TrySelectHotbarSlot(Connection, (byte)Enumerable.Range(0, 8).First(i => !taken.Contains((ushort)i))).Should().Be(OpStatus.Ok);
                StrikeUntilGone(world, tree, untilStump: true).Should().BeGreaterThan(1, "bare hands fell an oak slowly");
                PickUpNewLoot(world, sink);
            }

            uint fiber = items.ResolveId("plant_fiber");
            for (int round = 0; round < 4 && Count(world, fiber) < 8; round++)
            {
                StepTicks(world, 30 * ((int)world.PropRegrowth.SpawnForageRespawnSeconds + 1));
                foreach ((int X, int Y) bush in FindProps(mirror, spawnTile, 9, MapPropType.DryBush))
                {
                    WalkTo(world, sink, TileCenter(bush));
                    _ = StrikeUntilGone(world, bush);
                    PickUpNewLoot(world, sink);
                }
            }

            Count(world, StarterGameCatalog.WoodLog).Should().BeGreaterThanOrEqualTo(8, "branches plus two punched oaks");
            Count(world, fiber).Should().BeGreaterThanOrEqualTo(8);

            // 3. Every wood tool is a hand recipe: no station anywhere near the spawn clearing.
            Craft(world, "rope", 2);
            Craft(world, "wood_plank", 3);
            Craft(world, "wood_axe", 1);
            Craft(world, "wood_pickaxe", 1);
            Craft(world, "wood_sword", 1);
            uint pickaxe = items.ResolveId("wood_pickaxe");
            Count(world, StarterGameCatalog.WoodAxe).Should().Be(1);
            Count(world, pickaxe).Should().Be(1);
            Count(world, items.ResolveId("wood_sword")).Should().Be(1);

            // 4. A boulder resists bare hands but breaks to the wood pickaxe, dropping stone.
            (int X, int Y) boulder = FindProps(mirror, spawnTile, 60, MapPropType.Boulder).First();
            WalkTo(world, sink, TileCenter(boulder));
            HashSet<ushort> occupied = [.. Inventory(world).Select(s => s.SlotIndex)];
            byte emptyHand = (byte)Enumerable.Range(0, 8).First(i => !occupied.Contains((ushort)i));
            world.TrySelectHotbarSlot(Connection, emptyHand).Should().Be(OpStatus.Ok);
            StepTicks(world, 30);
            world.TryHarvestProp(Connection, boulder.X, boulder.Y).Status.Should().Be(OpStatus.Forbidden, "bare hands cannot mine a boulder");

            ushort pickSlot = Inventory(world).Single(s => s.Slot.ItemId == pickaxe).SlotIndex;
            world.TrySelectHotbarSlot(Connection, (byte)pickSlot).Should().Be(OpStatus.Ok);
            int stoneBefore = Count(world, StarterGameCatalog.StoneChunk);
            StrikeUntilGone(world, boulder).Should().BePositive("the wood pickaxe mines a boulder");
            PickUpNewLoot(world, sink);
            Count(world, StarterGameCatalog.StoneChunk).Should().BeGreaterThan(stoneBefore, "a mined boulder drops stone for the stone tier");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    /// <summary>Creates a fresh character the way the CharacterCreate handler does (at the world spawn) and enters it.</summary>
    private static async Task<(AuthoritativeWorld World, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink Sink, (int X, int Y) SpawnTile)> EnterFreshCharacterAsync(SqliteConnectionFactory factory)
    {
        await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
        SqliteAccountStore accounts = new(factory);
        SqliteCharacterStore characters = new(factory);
        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        AuthoritativeWorld world = new(Seed, TileSize, 0.0008f, 30, new SqliteChunkDeltaRepository(factory), characters, sink, entropy: TestGameplayEntropy.Create(Seed), propRegrowth: s_regrowth);
        await world.InitializeFromStoreAsync().ConfigureAwait(true);

        Vector2 spawn = world.DefaultSpawnWorldPosition;
        using AuthService auth = new(accounts, characters, new Pbkdf2PasswordHasher());
        AuthOutcome account = await auth.RegisterAsync("bootstrap", "secret-pass", CarbonProtocol.Version).ConfigureAwait(true);
        CharacterCreateResult created = await auth.CreateCharacterAsync(account.AccountId, "Bootstrap", TestLooks.Human, spawn.X, spawn.Y).ConfigureAwait(true);
        created.Succeeded.Should().BeTrue();
        CharacterRecord hero = (await characters.FindByIdAsync(created.Character!.Id).ConfigureAwait(true))!;
        world.TryEnterWorld(Connection, hero).Status.Should().Be(OpStatus.Ok);
        Inventory(world).Should().BeEmpty("a new character starts with nothing (ADR-030)");
        return (world, sink, ((int)MathF.Floor(spawn.X / TileSize), (int)MathF.Floor(spawn.Y / TileSize)));
    }

    private static void Craft(AuthoritativeWorld world, string recipeCode, ushort quantity)
    {
        world.ItemRules.RecipeCatalog.TryGetByCode(recipeCode, out CraftingRecipe? recipe).Should().BeTrue();
        world.TryCraftRecipe(Connection, (ushort)recipe!.Id, quantity).Should().Be(OpStatus.Ok, $"'{recipeCode}' is a hand recipe and its materials were gathered");
    }

    private static Vector2 TileCenter((int X, int Y) tile) => new((tile.X + 0.5f) * TileSize, (tile.Y + 0.5f) * TileSize);

    private static List<(int X, int Y)> FindProps(ChunkWorld2D world, (int X, int Y) center, int radius, MapPropType type)
    {
        List<(int X, int Y)> found = [];
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (world.TryGetPropAt(center.X + dx, center.Y + dy, out MapProp prop) && prop.Type == type)
                {
                    found.Add((center.X + dx, center.Y + dy));
                }
            }
        }

        found.Should().NotBeEmpty($"the seeded world has a {type} within {radius} tiles of spawn");
        return [.. found.OrderBy(t => ((t.X - center.X) * (t.X - center.X)) + ((t.Y - center.Y) * (t.Y - center.Y)))];
    }

    /// <summary>Steers the character with move inputs until it stands within a tile of <paramref name="target"/>.</summary>
    private static void WalkTo(AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink, Vector2 target)
    {
        uint clientTick = 1U;
        for (int tick = 0; tick < 30 * 60; tick++)
        {
            world.StepFixedTick();
            Vector2 at = sink.SelfPositions[Connection];
            Vector2 toTarget = target - at;
            if (toTarget.Length() <= TileSize)
            {
                world.EnqueueMoveInput(Connection, clientTick++, Vector2.Zero, sprint: false);
                world.StepFixedTick();
                return;
            }

            world.EnqueueMoveInput(Connection, clientTick++, Vector2.Normalize(toTarget), sprint: false);
        }

        throw new InvalidOperationException($"could not walk to {target}");
    }

    /// <summary>
    /// Strikes the prop by hand or with the selected tool until it breaks (a tree: until it is felled to a stump, when
    /// <paramref name="untilStump"/>); returns the strikes that landed.
    /// </summary>
    private static int StrikeUntilGone(AuthoritativeWorld world, (int X, int Y) tile, bool untilStump = false)
    {
        int landed = 0;
        for (int attempt = 0; attempt < 2000; attempt++)
        {
            if (untilStump && RegrowthWorldFixture.Probe(world, tile.X, tile.Y).Stage == TreeGrowthStage.Stump)
            {
                return landed;
            }

            OpStatus status = world.TryHarvestProp(Connection, tile.X, tile.Y).Status;
            if (status == OpStatus.NotFound)
            {
                return landed;
            }

            status.Should().BeOneOf([OpStatus.Ok, OpStatus.RateLimited], $"the prop at {tile} must not resist the strike");
            if (status == OpStatus.Ok)
            {
                landed++;
            }

            world.StepFixedTick();
        }

        throw new InvalidOperationException($"the prop at {tile} never broke");
    }

    private static void PickUpNewLoot(AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink)
    {
        world.StepFixedTick();
        ulong[] loot = [.. sink.Spawns
            .Where(s => s.ConnectionId == Connection && s.Snapshot.Kind == EntityKind.LootDrop)
            .Select(s => s.Snapshot.NetworkId)
            .Distinct()];
        foreach (ulong id in loot)
        {
            _ = world.EnqueuePickupLoot(Connection, id);
            world.StepFixedTick();
        }

        sink.Spawns.RemoveAll(s => s.Snapshot.Kind == EntityKind.LootDrop);
    }

    private static List<(ushort SlotIndex, ItemSlot Slot)> Inventory(AuthoritativeWorld world)
    {
        List<(ushort SlotIndex, ItemSlot Slot)> slots = [];
        world.GetInventorySnapshot(Connection, slots).Should().BeTrue();
        return [.. slots.Where(s => !s.Slot.IsEmpty)];
    }

    private static int Count(AuthoritativeWorld world, uint itemId) =>
        Inventory(world).Where(s => s.Slot.ItemId == itemId).Sum(s => s.Slot.Quantity);

    private static void StepTicks(AuthoritativeWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            world.StepFixedTick();
        }
    }
}

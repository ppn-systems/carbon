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
/// A newly created character enters the world with nothing and must reach its first tool with server commands alone: gather loose
/// stones, branches and fiber by hand at spawn, craft rope and the stone axe, and then fell trees faster than by hand.
/// </summary>
public sealed class SurvivalBootstrapWorldTests
{
    private const ulong Seed = 1337UL;
    private const float TileSize = 32f;
    private const ulong Connection = 811UL;

    [Fact]
    public async Task EmptyHandedCharacter_GathersAtSpawnCraftsStoneAxe_AndThenChopsATreeFaster()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-bootstrap-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            SqliteAccountStore accounts = new(factory);
            SqliteCharacterStore characters = new(factory);
            AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();

            AuthoritativeWorld world = new(Seed, TileSize, 0.0008f, 30, new SqliteChunkDeltaRepository(factory), characters, sink, entropy: TestGameplayEntropy.Create(Seed));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            // A fresh character, created the way the CharacterCreate handler creates one (at the world spawn).
            Vector2 spawn = world.DefaultSpawnWorldPosition;
            using AuthService auth = new(accounts, characters, new Pbkdf2PasswordHasher());
            AuthOutcome account = await auth.RegisterAsync("bootstrap", "secret-pass", CarbonProtocol.Version).ConfigureAwait(true);
            CharacterCreateResult created = await auth.CreateCharacterAsync(account.AccountId, "Bootstrap", 0, spawn.X, spawn.Y).ConfigureAwait(true);
            created.Succeeded.Should().BeTrue();
            CharacterRecord hero = (await characters.FindByIdAsync(created.Character!.Id).ConfigureAwait(true))!;
            world.TryEnterWorld(Connection, hero).Status.Should().Be(OpStatus.Ok);
            Inventory(world).Should().BeEmpty("a new character starts with nothing (ADR-030)");

            // 1. Gather the stone axe's materials by hand from the forage the generator leaves at spawn.
            ChunkWorld2D mirror = new(new ProceduralWorldGenerator(Seed), TileSize, 0.0008f);
            (int X, int Y) spawnTile = ((int)MathF.Floor(spawn.X / TileSize), (int)MathF.Floor(spawn.Y / TileSize));
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

            // 2. Craft rope and the stone axe without a station.
            world.ItemRules.RecipeCatalog.TryGetByCode("rope", out CraftingRecipe? rope).Should().BeTrue();
            world.ItemRules.RecipeCatalog.TryGetByCode("wood_axe", out CraftingRecipe? axe).Should().BeTrue();
            world.TryCraftRecipe(Connection, (ushort)rope!.Id, 1).Should().Be(OpStatus.Ok, "rope is a hand recipe");
            world.TryCraftRecipe(Connection, (ushort)axe!.Id, 1).Should().Be(OpStatus.Ok, "the stone axe is a hand recipe");
            (ushort axeSlot, ItemSlot _) = Inventory(world).Single(s => s.Slot.ItemId == StarterGameCatalog.WoodAxe);
            Count(world, StarterGameCatalog.WoodLog).Should().Be(0, "the axe used up the gathered logs");

            // 3. A tier-1 tree falls to bare hands, slowly; once it regrows the axe fells it in fewer strikes.
            (int X, int Y) tree = FindProps(mirror, spawnTile, 40, MapPropType.OakTree).First();
            WalkTo(world, sink, TileCenter(tree));
            HashSet<ushort> occupied = [.. Inventory(world).Select(s => s.SlotIndex)];
            byte emptyHand = (byte)Enumerable.Range(0, 8).First(i => !occupied.Contains((ushort)i));
            world.TrySelectHotbarSlot(Connection, emptyHand).Should().Be(OpStatus.Ok);
            int punches = StrikeUntilGone(world, tree);
            PickUpNewLoot(world, sink);
            Count(world, StarterGameCatalog.WoodLog).Should().BePositive("a punched-down tree drops its logs");

            StepTicks(world, 30 * 31);
            world.TrySelectHotbarSlot(Connection, (byte)axeSlot).Should().Be(OpStatus.Ok);
            int chops = StrikeUntilGone(world, tree);

            punches.Should().BeGreaterThan(1, "bare hands fell a tree slowly");
            chops.Should().BeLessThan(punches, "the stone axe chops faster than bare hands");
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

    /// <summary>Strikes the prop by hand or with the selected tool until it breaks; returns the strikes that landed.</summary>
    private static int StrikeUntilGone(AuthoritativeWorld world, (int X, int Y) tile)
    {
        int landed = 0;
        for (int attempt = 0; attempt < 2000; attempt++)
        {
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

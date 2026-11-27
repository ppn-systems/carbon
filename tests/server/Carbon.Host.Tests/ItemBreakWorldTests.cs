// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-035 through the authoritative world: a tool's last swing destroys it in the same tick, the owner gets the empty
/// slot and a break alert, and the empty slot is saved; a legacy save holding a 0-durability item loads it back at 1.
/// </summary>
public sealed class ItemBreakWorldTests
{
    private const ulong Seed = 1337UL;
    private const ulong Conn = 935UL;

    [Fact]
    public async Task TryHarvestProp_AxeOnItsLastSwing_IsDestroyedTheOwnerGetsTheEmptySlotAndABreakAndItIsSaved()
    {
        using Fixture f = await Fixture.CreateAsync(slot0: new ItemSlot(StarterGameCatalog.WoodAxe, 1, InstanceId: 77, Durability: 0.5f)).ConfigureAwait(true);

        f.World.TryHarvestProp(Conn, f.PropTileX, f.PropTileY).Status.Should().NotBe(OpStatus.OutOfRange, "the hero stands beside the prop");

        f.Snapshot().Should().NotContain(s => s.SlotIndex == 0 && !s.Slot.IsEmpty, "ADR-035: the swing that takes the axe to 0 destroys it");
        f.Sink.SlotChanges.Should().Contain((Conn, 0, 0U, 0), "the owner learns the slot is empty");
        f.World.StepFixedTick();
        f.Sink.DurabilityAlerts.Should().ContainSingle().Which.Should().Be((Conn, DurabilityAlertKind.Broke, StarterGameCatalog.WoodAxe, 0));
        f.SavedInventory().GetSlot(0).IsEmpty.Should().BeTrue("the character was marked dirty, so the break is persisted");
    }

    [Fact]
    public async Task TryEnterWorld_LegacySaveWithAWornOutSword_RestoresItToOneDurabilityAndSavesIt()
    {
        using Fixture f = await Fixture.CreateAsync(
            slot0: new ItemSlot(StarterGameCatalog.IronSword, 1, InstanceId: 78, Durability: 0f, UpgradeLevel: 6),
            slot1: new ItemSlot(StarterGameCatalog.WoodLog, 5)).ConfigureAwait(true);

        List<(ushort SlotIndex, ItemSlot Slot)> slots = f.Snapshot();
        slots.Should().Contain((0, new ItemSlot(StarterGameCatalog.IronSword, 1, InstanceId: 78, Durability: 1f, UpgradeLevel: 6)),
            "ADR-035 D7: the worn-out +6 sword survives with 1 durability instead of being deleted");
        slots.Should().Contain((1, new ItemSlot(StarterGameCatalog.WoodLog, 5)), "items without durability are untouched");
        await f.World.FlushDirtyPersistenceAsync().ConfigureAwait(true);
        CharacterRecord? stored = await f.Characters.FindByIdAsync(f.HeroId).ConfigureAwait(true);
        InventoryContainer reloaded = new(PlayerInventoryLayout.TotalCapacity);
        InventoryBinaryCodec.TryDeserializeInto(stored!.InventoryBlob, reloaded).Should().BeTrue();
        reloaded.GetSlot(0).Durability.Should().Be(1f, "the fix-up is written to the store, so the next login does not repeat it");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnectionFactory _factory;

        private Fixture(string dbPath, SqliteConnectionFactory factory, AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink, SqliteCharacterStore characters, Guid heroId, int propTileX, int propTileY)
        {
            this.Characters = characters;
            _dbPath = dbPath;
            _factory = factory;
            this.World = world;
            this.Sink = sink;
            this.HeroId = heroId;
            this.PropTileX = propTileX;
            this.PropTileY = propTileY;
        }

        public AuthoritativeWorld World { get; }

        public AuthoritativeWorldMultiplayerTests.RecordingOutboundSink Sink { get; }

        public SqliteCharacterStore Characters { get; }

        public Guid HeroId { get; }

        public int PropTileX { get; }

        public int PropTileY { get; }

        public static async Task<Fixture> CreateAsync(ItemSlot slot0, ItemSlot slot1 = default)
        {
            string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-item-break-{Guid.NewGuid():N}.db");
            SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(false);

            SqliteAccountStore accounts = new(factory);
            SqliteCharacterStore characters = new(factory);
            DateTimeOffset now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
            Guid accountId = Guid.NewGuid();
            _ = await accounts.TryCreateAsync(new AccountRecord(accountId, "breaker", "BREAKER", [1], [2], now, false)).ConfigureAwait(false);

            AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
            AuthoritativeWorld world = new(
                Seed,
                32f,
                0.0008f,
                30,
                new SqliteChunkDeltaRepository(factory),
                characters,
                sink,
                entropy: TestGameplayEntropy.Create(Seed));
            await world.InitializeFromStoreAsync().ConfigureAwait(false);

            (int tx, int ty) = FindDestructibleProp(world);
            InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
            inv.SetSlot(0, in slot0);
            if (!slot1.IsEmpty)
            {
                inv.SetSlot(1, in slot1);
            }

            CharacterRecord hero = new(
                Guid.NewGuid(),
                accountId,
                "Breaker",
                Level: 1,
                AppearancePreset: 0,
                PositionX: ((tx + 0.5f) * 32f) + 40f,
                PositionY: (ty + 0.5f) * 32f,
                CurrentHealth: 100f,
                MaxHealth: 100f,
                SelectedHotbarSlot: 0,
                InventoryBlob: InventoryBinaryCodec.Serialize(inv),
                UpdatedAtUtc: now,
                Flags: CharacterTraits.StarterLoadoutGranted);
            _ = await characters.TryCreateAsync(hero).ConfigureAwait(false);
            world.TryEnterWorld(Conn, hero).Status.Should().Be(OpStatus.Ok);

            return new Fixture(dbPath, factory, world, sink, characters, hero.Id, tx, ty);
        }

        public List<(ushort SlotIndex, ItemSlot Slot)> Snapshot()
        {
            List<(ushort SlotIndex, ItemSlot Slot)> slots = [];
            this.World.GetInventorySnapshot(Conn, slots).Should().BeTrue();
            return slots;
        }

        public InventoryContainer SavedInventory()
        {
            this.World.TryGetPendingCharacterSnapshot(this.HeroId, out CharacterRecord? record).Should().BeTrue("the character must be marked dirty");
            InventoryContainer inventory = new(PlayerInventoryLayout.TotalCapacity);
            InventoryBinaryCodec.TryDeserializeInto(record!.InventoryBlob, inventory).Should().BeTrue();
            return inventory;
        }

        public void Dispose()
        {
            _factory.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }

        private static (int X, int Y) FindDestructibleProp(AuthoritativeWorld world)
        {
            // Mirror the server's deterministic generator to find a destructible prop near spawn.
            ChunkWorld2D mirror = new(new ProceduralWorldGenerator(Seed), 32f, 0.0008f);
            int sx = (int)MathF.Floor(world.DefaultSpawnWorldPosition.X / 32f);
            int sy = (int)MathF.Floor(world.DefaultSpawnWorldPosition.Y / 32f);
            for (int r = 0; r <= 64; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r &&
                            mirror.TryGetPropAt(sx + dx, sy + dy, out MapProp prop) &&
                            prop.Destructibility.IsDestructible && !prop.Destructibility.IsDestroyed)
                        {
                            return (sx + dx, sy + dy);
                        }
                    }
                }
            }

            throw new InvalidOperationException("No destructible prop near spawn for the test seed.");
        }
    }
}

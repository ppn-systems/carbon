// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World.Needs;
using Carbon.Application.World.Persistence;
using Carbon.Application.World.Players;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Maps.Building;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Carbon.Survival.Config;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class CharacterPersistenceTests
{
    [Fact]
    public void LoadInventory_UnreadableBlob_GrantsTheStarterLoadoutAndFlagsIt()
    {
        CharacterRecord fresh = Character() with { InventoryBlob = [], Flags = CharacterTraits.None };
        InventoryContainer inventory = new(PlayerInventoryLayout.TotalCapacity);

        CharacterRecord loaded = CharacterPersistence.LoadInventory(fresh, inventory);

        loaded.Flags.HasFlag(CharacterTraits.StarterLoadoutGranted).Should().BeTrue();
        inventory.Slots.ToArray().Should().Contain(s => !s.IsEmpty);
    }

    [Fact]
    public void LoadInventory_SavedBlob_RestoresItUnchanged()
    {
        InventoryContainer saved = new(PlayerInventoryLayout.TotalCapacity);
        saved.SetSlot(3, new ItemSlot(4U, 7));
        CharacterRecord record = Character() with { InventoryBlob = InventoryBinaryCodec.Serialize(saved), Flags = CharacterTraits.None };
        InventoryContainer inventory = new(PlayerInventoryLayout.TotalCapacity);

        CharacterRecord loaded = CharacterPersistence.LoadInventory(record, inventory);

        loaded.Should().BeSameAs(record);
        inventory.GetSlot(3).Should().Be(new ItemSlot(4U, 7));
    }

    [Fact]
    public void LoadProgression_LegacySkillMaskOnly_IsCarriedOver()
    {
        CharacterRecord legacy = Character() with { ProgressionBlob = null, UnlockedSkills = (CharacterSkillMask)0b101UL };

        CharacterPersistence.LoadProgression(legacy).UnlockedSkillMask.Should().Be((CharacterSkillMask)0b101UL);
    }

    [Fact]
    public void MarkDirtyThenFlush_SavesTheSnapshotAndKeepsItPendingUntilReleased()
    {
        Harness h = new();
        PlayerState player = h.Join(new Vector2(40f, 0f));
        player.Level = 7;

        h.Persistence.MarkDirty(player, inventoryChanged: true);
        List<CharacterRecord> batch = [];
        h.Persistence.CollectBatch(h.Players.Values, batch);

        CharacterRecord saved = batch.Should().ContainSingle().Subject;
        saved.Level.Should().Be(7);
        saved.PositionX.Should().Be(40f);
        player.PersistenceDirty.Should().BeFalse();
        player.InventoryDirty.Should().BeFalse();

        h.Players.Clear();
        h.Persistence.TryGetPending(h.Players.Values, saved.Id, out CharacterRecord? pending).Should().BeTrue("the save has not committed yet");
        pending.Should().BeSameAs(saved);

        h.Persistence.Release(batch);
        h.Persistence.TryGetPending(h.Players.Values, saved.Id, out _).Should().BeFalse();
    }

    [Fact]
    public void Requeue_FailedSave_IsSavedAgainByTheNextFlush()
    {
        Harness h = new();
        PlayerState player = h.Join(Vector2.Zero);
        h.Persistence.MarkDirty(player, inventoryChanged: false);
        List<CharacterRecord> failed = [];
        h.Persistence.CollectBatch(h.Players.Values, failed);
        h.Players.Clear();

        h.Persistence.Requeue(failed);
        List<CharacterRecord> retry = [];
        h.Persistence.CollectBatch(h.Players.Values, retry);

        retry.Should().ContainSingle().Which.Should().BeSameAs(failed[0]);
    }

    [Fact]
    public void CollectBatch_PlayerMovedFourPixelsSinceTheLastSave_IsSnapshotted()
    {
        Harness h = new();
        PlayerState player = h.Join(Vector2.Zero);
        h.Simulation.Entities.SetTransform(player.Entity, new Transform2D(new Vector2(3f, 0f)));
        List<CharacterRecord> batch = [];
        h.Persistence.CollectBatch(h.Players.Values, batch);
        batch.Should().BeEmpty("3px is below the save threshold");

        h.Simulation.Entities.SetTransform(player.Entity, new Transform2D(new Vector2(4f, 0f)));
        h.Persistence.CollectBatch(h.Players.Values, batch);

        batch.Should().ContainSingle().Which.PositionX.Should().Be(4f);
    }

    [Fact]
    public void TryGetPending_PlayerInTheWorld_BuildsTheLiveRecord()
    {
        Harness h = new();
        PlayerState player = h.Join(new Vector2(12f, 34f));

        h.Persistence.TryGetPending(h.Players.Values, player.SourceCharacter.Id, out CharacterRecord? live).Should().BeTrue();

        (live!.PositionX, live.PositionY).Should().Be((12f, 34f));
    }

    private static CharacterRecord Character() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Tester", 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);

    private sealed class Harness
    {
        private ulong _nextNetworkId = 600UL;

        public Harness()
        {
            ItemRules rules = ItemRules.LoadShippedDefaults();
            NeedsService needs = new(this.Simulation, new BuildingGrid(), 32f, () => rules, () => null, new NeedsConfig(), null, TestGameplayEntropy.Create(1UL));
            this.Persistence = new CharacterPersistence(this.Simulation, needs, TimeProvider.System);
        }

        public WorldSimulation Simulation { get; } = new();

        public Dictionary<ulong, PlayerState> Players { get; } = new();

        public CharacterPersistence Persistence { get; }

        public PlayerState Join(Vector2 position)
        {
            ulong netId = ++_nextNetworkId;
            CharacterRecord character = Character() with { PositionX = position.X, PositionY = position.Y };
            ConnectedPlayerSession session = new(1UL, netId, character.AccountId, character.Id, character.Name, netId, 0, 0);
            PlayerState state = new(session, this.Simulation.SpawnNetworkEntity(netId, position, 7f), character, new InventoryContainer(4))
            {
                LastSavedPosition = position,
            };
            this.Players[netId] = state;
            return state;
        }
    }
}

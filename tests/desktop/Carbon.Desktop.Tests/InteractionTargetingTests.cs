// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Gameplay.Interaction;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Tests.Fakes;
using Carbon.Items.Catalog;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

public sealed class InteractionTargetingTests
{
    private const ushort TraderArchetype = 901;
    private const ushort SmithArchetype = 902;
    private const ulong TraderId = 77UL;

    private static readonly StructureCatalog s_structures = StructureCatalog.LoadEmbedded();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectNearest_DoorNearerThanNpc_PicksTheDoorInEitherOrder(bool doorListedFirst)
    {
        InteractionCandidate door = new(new InteractionTarget(InteractionKind.OpenDoor, new Vector2(32f, 0f), 0UL, 1, 0), 80f);
        InteractionCandidate npc = new(new InteractionTarget(InteractionKind.Trade, new Vector2(70f, 0f), TraderId, 0, 0), 150f);
        InteractionCandidate[] candidates = doorListedFirst ? [door, npc] : [npc, door];

        InteractionTarget target = InteractionTargeting.SelectNearest(Vector2.Zero, candidates);

        target.Kind.Should().Be(InteractionKind.OpenDoor, "the door is nearer than the trader");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectNearest_NpcNearerThanDoor_PicksTheNpcInEitherOrder(bool doorListedFirst)
    {
        InteractionCandidate door = new(new InteractionTarget(InteractionKind.OpenDoor, new Vector2(64f, 0f), 0UL, 2, 0), 80f);
        InteractionCandidate npc = new(new InteractionTarget(InteractionKind.Trade, new Vector2(-30f, 0f), TraderId, 0, 0), 150f);
        InteractionCandidate[] candidates = doorListedFirst ? [door, npc] : [npc, door];

        InteractionTarget target = InteractionTargeting.SelectNearest(Vector2.Zero, candidates);

        target.Kind.Should().Be(InteractionKind.Trade, "the trader is nearer than the door");
        target.NetworkId.Should().Be(TraderId);
    }

    [Fact]
    public void SelectNearest_NearestIsOutsideItsOwnRadius_PicksTheNextUsableOne()
    {
        InteractionCandidate drop = new(new InteractionTarget(InteractionKind.PickUp, new Vector2(40f, 0f), 5UL, 0, 0), 20f);
        InteractionCandidate npc = new(new InteractionTarget(InteractionKind.Trade, new Vector2(90f, 0f), TraderId, 0, 0), 150f);

        InteractionTargeting.SelectNearest(Vector2.Zero, [drop, npc]).Kind.Should().Be(InteractionKind.Trade, "the drop is out of its pick-up reach");
    }

    [Fact]
    public void SelectNearest_NothingInRange_ReturnsNone()
    {
        InteractionCandidate far = new(new InteractionTarget(InteractionKind.OpenDoor, new Vector2(500f, 0f), 0UL, 15, 0), 80f);

        InteractionTargeting.SelectNearest(Vector2.Zero, [far]).Exists.Should().BeFalse();
        InteractionTargeting.SelectNearest(Vector2.Zero, ReadOnlySpan<InteractionCandidate>.Empty).Exists.Should().BeFalse();
    }

    // Regression: inside a house, E talked to the villager instead of opening the door next to the player. The old chain
    // tried traders (150 px, straight-line) before doors (a 5x5 tile square), so any trader in the room won.
    [Fact]
    public void TryInteract_InsideAHouseNextToTheDoor_OpensTheDoorNotTheVillager()
    {
        (RecordingWorldSession session, float tile) = NewWorld();
        PlaceDoor(session, 10, 5);
        session.PlayerPosition = TileCentre(10, 4, tile);
        session.Remotes.Add(Npc(TraderId, TraderArchetype, TileCentre(12, 3, tile)));
        InteractionController interaction = new(session);

        InteractionTarget target = interaction.Refresh();
        bool acted = interaction.TryInteract(() => throw new InvalidOperationException("no forge here"));

        target.Kind.Should().Be(InteractionKind.OpenDoor, "the door is one tile away, the villager more than two");
        acted.Should().BeTrue();
        session.Commands.Should().Equal(["Door(10,5)"], "E must open the adjacent door, not talk to the villager");
    }

    [Fact]
    public void TryInteract_VillagerNearerThanTheDoor_TalksToTheVillager()
    {
        (RecordingWorldSession session, float tile) = NewWorld();
        PlaceDoor(session, 10, 5);
        session.PlayerPosition = TileCentre(10, 3, tile);
        session.Remotes.Add(Npc(TraderId, TraderArchetype, TileCentre(11, 3, tile)));
        InteractionController interaction = new(session);

        _ = interaction.Refresh();
        _ = interaction.TryInteract(() => false);

        session.Commands.Should().Equal([$"Trade({TraderId})"], "the villager is one tile away, the door (also in reach) two");
    }

    // The prompt and the key read the same target: whatever the layout, the prompt names exactly what E then does.
    [Fact]
    public void BuildPrompt_ForEveryPlayerPosition_NamesTheTargetThatTryInteractActsOn()
    {
        (RecordingWorldSession session, float tile) = NewWorld();
        PlaceDoor(session, 10, 5);
        PlaceDoor(session, 14, 5);
        PlaceStation(session, StructureType.ForgeAnvil, 8, 9);
        session.Remotes.Add(Npc(TraderId, TraderArchetype, TileCentre(12, 3, tile)));
        session.Remotes.Add(Npc(78UL, SmithArchetype, TileCentre(16, 8, tile)));
        session.Remotes.Add(Loot(79UL, TileCentre(11, 7, tile), locked: false));
        session.Remotes.Add(Loot(80UL, TileCentre(13, 6, tile), locked: true));
        InteractionController interaction = new(session);
        int forgeOpened = 0;
        int seen = 0;

        for (float y = 0f; y <= 12f; y += 0.5f)
        {
            for (float x = 4f; x <= 20f; x += 0.5f)
            {
                session.PlayerPosition = new Vector2(x * tile, y * tile);
                session.Commands.Clear();
                forgeOpened = 0;
                InteractionTarget target = interaction.Refresh();
                InteractionPrompt? prompt = interaction.BuildPrompt(static p => p);
                bool acted = interaction.TryInteract(() => ++forgeOpened > 0);

                if (!target.Exists)
                {
                    prompt.Should().BeNull("nothing in reach shows no prompt");
                    acted.Should().BeFalse();
                    session.Commands.Should().BeEmpty("nothing in reach does nothing");
                    continue;
                }

                seen++;
                prompt.Should().NotBeNull();
                prompt!.Value.Kind.Should().Be(target.Kind, "the prompt shows what E does");
                string expected = target.Kind switch
                {
                    InteractionKind.PickUp => $"PickUp({target.NetworkId})",
                    InteractionKind.Trade => $"Trade({target.NetworkId})",
                    InteractionKind.OpenDoor or InteractionKind.CloseDoor => $"Door({target.TileX},{target.TileY})",
                    InteractionKind.Forge or InteractionKind.None => string.Empty,
                    _ => string.Empty,
                };
                if (target.Kind == InteractionKind.Forge)
                {
                    forgeOpened.Should().Be(1, "E opens the forge the prompt shows");
                    session.Commands.Should().BeEmpty();
                }
                else
                {
                    session.Commands.Should().Equal([expected], $"at {x},{y} the prompt named {target.Kind}");
                }

                target.NetworkId.Should().NotBe(80UL, "loot reserved for another player is never a target");
            }
        }

        seen.Should().BeGreaterThan(100, "the sweep must cross every kind of target");
    }

    [Fact]
    public void Refresh_NothingInRange_ShowsNoPromptAndEDoesNothing()
    {
        (RecordingWorldSession session, float tile) = NewWorld();
        PlaceDoor(session, 10, 5);
        session.Remotes.Add(Npc(TraderId, TraderArchetype, TileCentre(12, 3, tile)));
        session.PlayerPosition = TileCentre(40, 40, tile);
        InteractionController interaction = new(session);

        interaction.Refresh().Exists.Should().BeFalse();
        interaction.BuildPrompt(static p => p).Should().BeNull();
        interaction.TryInteract(() => throw new InvalidOperationException("no forge here")).Should().BeFalse();
        session.Commands.Should().BeEmpty();
    }

    [Fact]
    public void TryToggleNearestDoor_TraderNearer_StillTogglesTheNearestDoor()
    {
        (RecordingWorldSession session, float tile) = NewWorld();
        PlaceDoor(session, 10, 5);
        PlaceDoor(session, 12, 5);
        session.PlayerPosition = TileCentre(12, 3, tile) + new Vector2(0f, 8f);
        session.Remotes.Add(Npc(TraderId, TraderArchetype, TileCentre(12, 3, tile)));
        InteractionController interaction = new(session);

        interaction.TryToggleNearestDoor().Should().BeTrue();

        session.Commands.Should().Equal(["Door(12,5)"], "the door key ignores NPCs and takes the nearest door");
    }

    private static (RecordingWorldSession Session, float TileSize) NewWorld()
    {
        RecordingWorldSession session = new() { World = new ChunkWorld2D(new ProceduralWorldGenerator(1337UL)) };
        session.NpcRoles[TraderArchetype] = NpcCapabilities.Trader;
        session.NpcRoles[SmithArchetype] = NpcCapabilities.Trader | NpcCapabilities.ForgeArtisan;
        return (session, session.ChunkWorld.TileSize);
    }

    private static void PlaceDoor(RecordingWorldSession session, int x, int y) => PlaceStation(session, StructureType.WoodDoor, x, y);

    private static void PlaceStation(RecordingWorldSession session, StructureType type, int x, int y)
    {
        s_structures.TryGet(type, out StructureDefinition definition).Should().BeTrue();
        StructureTile tile = definition.CreateTile();
        session.ChunkWorld.Buildings.PlaceStructure(x, y, in tile);
    }

    private static Vector2 TileCentre(int x, int y, float tile) => new((x + 0.5f) * tile, (y + 0.5f) * tile);

    private static RemoteEntityView Npc(ulong id, ushort archetype, Vector2 position) =>
        new(id, EntityKind.Npc, "Villager", position, Vector2.Zero, 10f, 100f, 100f, ArchetypeId: archetype);

    private static RemoteEntityView Loot(ulong id, Vector2 position, bool locked) =>
        new(id, EntityKind.LootDrop, "Stone", position, Vector2.Zero, 6f, 1f, 1f, LootCount: 1, IsLootLocked: locked);
}

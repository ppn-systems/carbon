// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Networking.Session;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Behaviour tests for the map-mutation handling that moved out of <c>MultiplayerSession</c> (audit round 2, SRP-1):
/// a real seeded <see cref="ChunkWorld2D"/>, the real item rules and the real localized warnings, with the session's
/// output lists observed directly.
/// </summary>
public sealed class MapMutationApplierTests
{
    private const byte HotbarSlot = 0;

    private readonly List<(int ChunkX, int ChunkY)> _dirtyChunks = [];
    private readonly List<PropImpactEvent> _impacts = [];
    private readonly List<CombatFeedbackEvent> _combat = [];
    private readonly List<string> _hud = [];
    private readonly SyncedItemCatalog _items = new();
    private readonly InventoryContainer _inventory = new(PlayerInventoryLayout.TotalCapacity);
    private readonly ChunkWorld2D _world;
    private readonly MapMutationApplier _applier;

    public MapMutationApplierTests()
    {
        _world = new ChunkWorld2D(new ProceduralWorldGenerator(1337UL));
        _world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 2, evictionRadius: 4);
        _applier = new MapMutationApplier(_dirtyChunks, _impacts, _combat, _hud);
    }

    [Fact]
    public void ApplyPropChanged_NoPropOnTile_OnlyMarksTheChunkDirty()
    {
        (int x, int y) = this.FindEmptyTile();

        this.ApplyProp(x, y, durability: 5f, isDestroyed: false);

        _dirtyChunks.Should().Equal((Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y)));
        _impacts.Should().BeEmpty();
        _combat.Should().BeEmpty();
        _hud.Should().BeEmpty();
    }

    [Fact]
    public void ApplyPropChanged_Damaged_UpdatesWorldAndQueuesImpactAndDamageNumber()
    {
        (int x, int y, MapProp prop) = this.FindProp(PropCategory.Flora, minTier: 0);
        this.Hold(this.FindChopTool(prop.Destructibility.HarvestTier));
        float before = prop.Destructibility.CurrentDurability;

        this.ApplyProp(x, y, before - 10f, isDestroyed: false);

        _world.TryGetPropAt(x, y, out MapProp after).Should().BeTrue();
        after.Destructibility.CurrentDurability.Should().Be(before - 10f);
        _dirtyChunks.Should().Equal((Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y)));
        PropImpactEvent impact = _impacts.Should().ContainSingle().Subject;
        impact.TileX.Should().Be(x);
        impact.TileY.Should().Be(y);
        impact.DamageDealt.Should().BeApproximately(10f, 0.001f);
        impact.IsDestroyed.Should().BeFalse();
        impact.WasTierBlocked.Should().BeFalse();
        CombatFeedbackEvent number = _combat.Should().ContainSingle().Subject;
        number.Amount.Should().BeApproximately(10f, 0.001f);
        number.IsCritical.Should().BeFalse();
        number.WorldPosition.Should().Be(after.WorldPosition);
        _hud.Should().BeEmpty();
    }

    [Fact]
    public void ApplyPropChanged_Destroyed_QueuesACriticalNumberAndDestroysTheProp()
    {
        (int x, int y, MapProp prop) = this.FindProp(PropCategory.Flora, minTier: 0);
        this.Hold(this.FindChopTool(prop.Destructibility.HarvestTier));

        this.ApplyProp(x, y, durability: 0f, isDestroyed: true);

        _world.TryGetPropAt(x, y, out MapProp after).Should().BeTrue();
        after.Destructibility.IsDestroyed.Should().BeTrue();
        _impacts.Should().ContainSingle().Which.IsDestroyed.Should().BeTrue();
        _combat.Should().ContainSingle().Which.IsCritical.Should().BeTrue();
        _hud.Should().BeEmpty();
    }

    [Fact]
    public void ApplyPropChanged_DuplicateDestroyPacket_DoesNotWarnAboutTheToolTier()
    {
        // A second "destroyed" update deals no damage; it must not be mistaken for a tier-blocked strike.
        (int x, int y, MapProp prop) = this.FindProp(PropCategory.Mineral, minTier: 1);
        this.ApplyProp(x, y, durability: 0f, isDestroyed: true);
        _impacts.Clear();
        _combat.Clear();

        this.ApplyProp(x, y, durability: 0f, isDestroyed: true);

        prop.Destructibility.HarvestTier.Should().BeGreaterThan(0);
        _impacts.Should().ContainSingle().Which.WasTierBlocked.Should().BeFalse();
        _combat.Should().BeEmpty();
        _hud.Should().BeEmpty();
    }

    [Fact]
    public void ApplyPropChanged_NoDamageOnTieredMineralBareHanded_WarnsWithTheRequiredTier()
    {
        (int x, int y, MapProp prop) = this.FindProp(PropCategory.Mineral, minTier: 1);
        byte tier = prop.Destructibility.HarvestTier;
        string expected = ToolWarningText.Describe(ToolStrikeOutcome.InsufficientTier, tier);

        this.ApplyProp(x, y, prop.Destructibility.CurrentDurability, isDestroyed: false);

        expected.Should().NotBeNullOrWhiteSpace();
        PropImpactEvent impact = _impacts.Should().ContainSingle().Subject;
        impact.WasTierBlocked.Should().BeTrue();
        impact.DamageDealt.Should().Be(0f);
        CombatFeedbackEvent warning = _combat.Should().ContainSingle().Subject;
        warning.Label.Should().Be(expected);
        warning.Amount.Should().Be(0f);
        _hud.Should().Equal(expected);
    }

    [Fact]
    public void ApplyPropChanged_NoDamageWithASuitableTool_StillReportsTheTierWarning()
    {
        // The server refused the strike even though the client thinks the tool is fine: show the tier warning anyway.
        (int x, int y, MapProp prop) = this.FindProp(PropCategory.Flora, minTier: 0);
        byte tier = prop.Destructibility.HarvestTier;
        this.Hold(this.FindChopTool(tier));

        this.ApplyProp(x, y, prop.Destructibility.CurrentDurability, isDestroyed: false);

        _hud.Should().Equal(ToolWarningText.Describe(ToolStrikeOutcome.InsufficientTier, tier));
        _impacts.Should().ContainSingle().Which.WasTierBlocked.Should().BeTrue();
    }

    [Fact]
    public void ApplyPropChanged_SwordChopsWood_QueuesTheDamageAndTheMisuseWarning()
    {
        (int x, int y, MapProp prop) = this.FindProp(PropCategory.Flora, minTier: 0);
        this.Hold(this.FindItem(static def => def.Category == ItemCategory.Weapon));

        this.ApplyProp(x, y, prop.Destructibility.CurrentDurability - 5f, isDestroyed: false);

        _combat.Should().ContainSingle().Which.Amount.Should().BeApproximately(5f, 0.001f);
        _hud.Should().Equal(ToolWarningText.Describe(ToolStrikeOutcome.SwordMisuseOnWood, prop.Destructibility.HarvestTier));
    }

    [Fact]
    public void ApplyPropChanged_SwordChipsStone_QueuesTheDamageWithoutTheWoodWarning()
    {
        // Stone is evaluated as a mineral strike (blunted blade), whose warning is not surfaced on a damaging hit.
        (int x, int y, MapProp prop) = this.FindProp(PropCategory.Mineral, minTier: 0);
        this.Hold(this.FindItem(static def => def.Category == ItemCategory.Weapon));

        this.ApplyProp(x, y, prop.Destructibility.CurrentDurability - 5f, isDestroyed: false);

        _combat.Should().ContainSingle().Which.Amount.Should().BeApproximately(5f, 0.001f);
        _hud.Should().BeEmpty();
    }

    [Fact]
    public void ApplyPropChanged_ChipUnderHalfAPoint_QueuesTheImpactOnly()
    {
        (int x, int y, MapProp prop) = this.FindProp(PropCategory.Flora, minTier: 0);
        this.Hold(this.FindItem(static def => def.Category == ItemCategory.Weapon));

        this.ApplyProp(x, y, prop.Destructibility.CurrentDurability - 0.3f, isDestroyed: false);

        _impacts.Should().ContainSingle().Which.WasTierBlocked.Should().BeFalse();
        _combat.Should().BeEmpty();
        _hud.Should().BeEmpty();
    }

    [Fact]
    public void ApplyStructureChanged_PlacesThenDemolishes_AndMarksTheChunkDirtyEachTime()
    {
        (int x, int y) = this.FindEmptyTile();
        (int, int) chunk = (Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y));

        _applier.ApplyStructureChanged(_world, x, y, (byte)StructureType.WoodWall, doorState: 0);

        _world.Buildings.TryGetStructure(x, y, out StructureTile wall).Should().BeTrue();
        wall.Type.Should().Be(StructureType.WoodWall);

        _applier.ApplyStructureChanged(_world, x, y, structureType: 0, doorState: 0);

        _world.Buildings.TryGetStructure(x, y, out _).Should().BeFalse();
        _dirtyChunks.Should().Equal(chunk, chunk);
    }

    [Theory]
    [InlineData(DoorState.Open)]
    [InlineData(DoorState.Closed)]
    public void ApplyStructureChanged_Door_EndsInTheAuthoritativeDoorState(DoorState state)
    {
        (int x, int y) = this.FindEmptyTile();

        _applier.ApplyStructureChanged(_world, x, y, (byte)StructureType.WoodDoor, (byte)state);

        _world.Buildings.TryGetStructure(x, y, out StructureTile door).Should().BeTrue();
        door.Category.Should().Be(StructureCategory.Door);
        door.DoorState.Should().Be(state);
    }

    private void ApplyProp(int x, int y, float durability, bool isDestroyed) =>
        _applier.ApplyPropChanged(_world, x, y, durability, isDestroyed, _inventory, HotbarSlot, _items);

    private void Hold(ItemArchetype item) =>
        _inventory.SetSlot(HotbarSlot, new ItemSlot(item.Id, 1, InstanceId: 1, Durability: MathF.Max(1f, item.MaxDurability)));

    private ItemArchetype FindChopTool(byte tier) => this.FindItem(def =>
        def.Category == ItemCategory.Tool && def.Capabilities.HasFlag(ToolCapabilities.ChopFlora) && def.HarvestTier >= tier);

    private ItemArchetype FindItem(Func<ItemArchetype, bool> match)
    {
        foreach (ItemArchetype def in _items.Rules.Items.All)
        {
            if (match(def))
            {
                return def;
            }
        }

        throw new InvalidOperationException("the item catalog has no matching item");
    }

    private (int X, int Y, MapProp Prop) FindProp(PropCategory category, byte minTier)
    {
        for (int y = -48; y < 48; y++)
        {
            for (int x = -48; x < 48; x++)
            {
                if (_world.TryGetPropAt(x, y, out MapProp prop) &&
                    prop.Category == category &&
                    prop.Destructibility is { IsDestructible: true, IsDestroyed: false, CurrentDurability: > 20f } d &&
                    d.HarvestTier >= minTier)
                {
                    return (x, y, prop);
                }
            }
        }

        throw new InvalidOperationException($"seed 1337 has no destructible {category} prop of tier >= {minTier} near spawn");
    }

    private (int X, int Y) FindEmptyTile()
    {
        for (int y = -48; y < 48; y++)
        {
            for (int x = -48; x < 48; x++)
            {
                if (!_world.TryGetPropAt(x, y, out _) && !_world.Buildings.TryGetStructure(x, y, out _))
                {
                    return (x, y);
                }
            }
        }

        throw new InvalidOperationException("no empty tile near spawn");
    }
}

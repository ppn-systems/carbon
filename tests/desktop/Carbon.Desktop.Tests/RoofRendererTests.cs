// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Rendering.Atmospherics;
using Carbon.Desktop.Rendering.World;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

// RimWorld-style roofs (ADR-027 revision 3): the whole roof of the building the player stands in is cut away, and roofs
// near the player fade, so interiors show from inside and from the doorstep.
public sealed class RoofRendererTests
{
    private static readonly StructureCatalog s_structures = StructureCatalog.LoadEmbedded();

    // A two-room house: west room interior x 11..13, east room x 15..17, rows 11..13, sharing the wall at x 14 with a door
    // in it at (14, 12) and the front door at (12, 14). A one-room house stands three tiles further east (x 22..24).
    [Fact]
    public void RoofAlphaAt_PlayerInOneRoom_CutsAwayTheRoofOfTheOtherRoomToo()
    {
        (RoofRenderer roofs, float tile) = NewVillage();

        Vector2 inWestRoom = Centre(12, 12, tile);

        roofs.RoofAlphaAt(12, 12, inWestRoom).Should().Be(0f, "the player's own room is open");
        roofs.RoofAlphaAt(16, 12, inWestRoom).Should().Be(0f, "the room behind the interior door belongs to the same building");
    }

    [Fact]
    public void RoofAlphaAt_PlayerInOneBuilding_KeepsTheNeighbourRoofed()
    {
        (RoofRenderer roofs, float tile) = NewVillage();

        float neighbour = roofs.RoofAlphaAt(23, 12, Centre(12, 12, tile));

        neighbour.Should().BeGreaterThan(0f, "only the player's own building is cut away");
    }

    [Fact]
    public void RoofAlphaAt_PlayerOnTheDoorstep_ShowsTheInteriorThroughAFadedRoof()
    {
        (RoofRenderer roofs, float tile) = NewVillage();

        float doorstep = roofs.RoofAlphaAt(12, 12, Centre(12, 15, tile));

        doorstep.Should().BeGreaterThan(0f).And.BeLessThan(0.5f, "right outside the door the roof is mostly see-through");
    }

    [Fact]
    public void RoofAlphaAt_WalkingAway_FadesInMonotonicallyAndIsOpaqueFarAway()
    {
        (RoofRenderer roofs, float tile) = NewVillage();
        float previous = 0f;

        for (float y = 14.6f; y <= 24f; y += 0.25f)
        {
            float alpha = roofs.RoofAlphaAt(12, 12, new Vector2(12.5f * tile, y * tile));
            alpha.Should().BeGreaterThanOrEqualTo(previous, $"walking away at row {y} never makes the roof more see-through");
            previous = alpha;
        }

        previous.Should().Be(1f, "a roof far from the player is drawn in full");
    }

    [Fact]
    public void FadeFor_IsZeroInsideAndRisesFromNearAlphaToOpaque()
    {
        RoofRenderer.FadeFor(inside: true, 0f).Should().Be(0f);
        RoofRenderer.FadeFor(inside: false, 0f).Should().Be(RoofRenderer.NearAlpha);
        RoofRenderer.FadeFor(inside: false, RoofRenderer.FadeStartTiles).Should().Be(1f);
        RoofRenderer.FadeFor(inside: false, RoofRenderer.FadeStartTiles * 0.5f).Should().BeInRange(RoofRenderer.NearAlpha, 1f);
    }

    // Village lights at night (ADR-041): every room glows softly, light spills out of front doors, and a smithy's stove
    // burns as a forge fire.
    [Fact]
    public void CollectLights_RoomsInView_GlowAndSpillLightOutOfTheFrontDoor()
    {
        (RoofRenderer roofs, float tile) = NewVillage();
        List<PointLight> lights = new();

        roofs.CollectLights(lights, 0f, 0f, 40f * tile, 30f * tile, 0f);

        Vector2 eastRoom = Centre(23, 12, tile);
        lights.Should().Contain(l => Vector2.Distance(l.Position, eastRoom) < tile, "the east house's room glows from its centre");
        Vector2 frontDoor = Centre(23, 14, tile);
        lights.Should().Contain(
            l => Vector2.Distance(l.Position, frontDoor) < tile && l.Position.Y > frontDoor.Y,
            "the front door spills light outward, south of the door");
    }

    [Fact]
    public void CollectLights_RoomsOutOfView_AddNothing()
    {
        (RoofRenderer roofs, float tile) = NewVillage();
        List<PointLight> lights = new();

        roofs.CollectLights(lights, 200f * tile, 200f * tile, 240f * tile, 230f * tile, 0f);

        lights.Should().BeEmpty("rooms far outside the view light nothing on screen");
    }

    [Fact]
    public void CollectLights_SmithyWithStove_BurnsAForgeFire()
    {
        (RoofRenderer roofs, float tile) = NewVillage(smithy: true);
        List<PointLight> lights = new();

        roofs.CollectLights(lights, 0f, 0f, 40f * tile, 30f * tile, 0f);

        Vector2 stove = Centre(24, 11, tile);
        lights.Should().Contain(
            l => Vector2.Distance(l.Position, stove) < 1f && l.Radius > 100f && l.Color.R > l.Color.B * 2,
            "a smithy's stove is a wide, orange forge fire");
    }

    private static (RoofRenderer Roofs, float TileSize) NewVillage(bool smithy = false)
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL));
        BuildingGrid grid = world.Buildings;
        Box(grid, 10, 10, 18, 14);
        for (int y = 11; y <= 13; y++)
        {
            Place(grid, y == 12 ? StructureType.WoodDoor : StructureType.WoodWall, 14, y);
        }

        Place(grid, StructureType.WoodDoor, 12, 14);
        Box(grid, 21, 10, 25, 14);
        Place(grid, StructureType.WoodDoor, 23, 14);
        if (smithy)
        {
            Place(grid, StructureType.Stove, 24, 11);
            Place(grid, StructureType.ForgeAnvil, 22, 11);
        }

        float tile = world.TileSize;
        RoofRenderer roofs = new(tile);
        roofs.Refresh(grid, 0f, 0f, 40f * tile, 30f * tile);
        return (roofs, tile);
    }

    private static void Box(BuildingGrid grid, int x0, int y0, int x1, int y1)
    {
        for (int x = x0; x <= x1; x++)
        {
            Place(grid, StructureType.WoodWall, x, y0);
            Place(grid, StructureType.WoodWall, x, y1);
        }

        for (int y = y0 + 1; y < y1; y++)
        {
            Place(grid, StructureType.WoodWall, x0, y);
            Place(grid, StructureType.WoodWall, x1, y);
        }
    }

    private static void Place(BuildingGrid grid, StructureType type, int x, int y)
    {
        s_structures.TryGet(type, out StructureDefinition definition).Should().BeTrue();
        StructureTile tile = definition.CreateTile();
        grid.PlaceStructure(x, y, in tile);
    }

    private static Vector2 Centre(int x, int y, float tile) => new((x + 0.5f) * tile, (y + 0.5f) * tile);
}

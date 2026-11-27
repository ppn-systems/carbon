// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Building;
using Carbon.Maps.Settlements;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class VillageBuildingGeneratorTests
{
    private const int Seeds = 300;

    private static readonly (int Dx, int Dy)[] s_steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    [Theory]
    [InlineData(VillageBuildingRole.House)]
    [InlineData(VillageBuildingRole.Smithy)]
    [InlineData(VillageBuildingRole.Store)]
    public void Generate_SameSeed_ReturnsTheSameBuilding(VillageBuildingRole role)
    {
        for (ulong seed = 1; seed <= 50; seed++)
        {
            VillageBuildingTemplate a = VillageBuildingGenerator.Generate(role, seed);
            VillageBuildingTemplate b = VillageBuildingGenerator.Generate(role, seed);

            Assert.True(a.HasSameFootprint(b), $"seed {seed} gave two different footprints");
            Assert.Equal(a.Furniture, b.Furniture);
            Assert.Equal(a.NpcSpot, b.NpcSpot);
        }
    }

    [Fact]
    public void Generate_Houses_VaryInSizeShapeAndRoomCount()
    {
        HashSet<(int, int)> sizes = [];
        HashSet<int> roomCounts = [];
        int lShapes = 0;
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            VillageBuildingTemplate t = VillageBuildingGenerator.Generate(VillageBuildingRole.House, seed);
            _ = sizes.Add((t.Width, t.Height));
            _ = roomCounts.Add(CountRooms(t));
            lShapes += IsLShaped(t) ? 1 : 0;
        }

        Assert.True(sizes.Count >= 10, $"only {sizes.Count} different house sizes");
        Assert.Contains(1, roomCounts);
        Assert.Contains(2, roomCounts);
        Assert.Contains(3, roomCounts);
        Assert.True(lShapes >= Seeds / 10, $"only {lShapes} of {Seeds} houses are L-shaped");
        Assert.True(lShapes <= Seeds * 6 / 10, $"{lShapes} of {Seeds} houses are L-shaped; rectangles should stay common");
    }

    [Theory]
    [InlineData(VillageBuildingRole.House)]
    [InlineData(VillageBuildingRole.Smithy)]
    [InlineData(VillageBuildingRole.Store)]
    public void Generate_EveryBuilding_IsEnclosedWithItsEntranceOnTheSouthWall(VillageBuildingRole role)
    {
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            VillageBuildingTemplate t = VillageBuildingGenerator.Generate(role, seed);
            int entrances = 0;
            for (int y = 0; y < t.Height; y++)
            {
                for (int x = 0; x < t.Width; x++)
                {
                    char c = t.CellAt(x, y);
                    if (c == VillageBuildingTemplate.FloorCell)
                    {
                        foreach ((int dx, int dy) in s_steps)
                        {
                            char n = t.CellAt(x + dx, y + dy);
                            Assert.True(
                                n is VillageBuildingTemplate.FloorCell or VillageBuildingTemplate.WallCell or VillageBuildingTemplate.DoorCell,
                                $"{t.Id} (seed {seed}) floor {x},{y} touches open ground");
                        }
                    }

                    if (c == VillageBuildingTemplate.DoorCell && t.CellAt(x, y + 1) == VillageBuildingTemplate.PorchCell)
                    {
                        entrances++;
                    }
                }
            }

            Assert.True(entrances == 1, $"{t.Id} (seed {seed}) has {entrances} entrances on its south wall");
            Assert.Equal(CountRooms(t), t.RoomCount);
        }
    }

    [Theory]
    [InlineData(VillageBuildingRole.House)]
    [InlineData(VillageBuildingRole.Smithy)]
    [InlineData(VillageBuildingRole.Store)]
    public void Generate_EveryRoomAndNpcSpot_IsReachableFromTheEntranceAroundTheFurniture(VillageBuildingRole role)
    {
        StructureCatalog structures = StructureCatalog.LoadEmbedded();
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            VillageBuildingTemplate t = VillageBuildingGenerator.Generate(role, seed);
            bool[] blocked = FurnitureMask(t, structures);
            HashSet<(int, int)> reach = Reach(t, blocked);
            for (int y = 0; y < t.Height; y++)
            {
                for (int x = 0; x < t.Width; x++)
                {
                    char c = t.CellAt(x, y);
                    bool free = c == VillageBuildingTemplate.FloorCell && !blocked[(y * t.Width) + x];
                    if (free || c == VillageBuildingTemplate.DoorCell)
                    {
                        Assert.True(reach.Contains((x, y)), $"{t.Id} (seed {seed}) cell {x},{y} '{c}' cannot be reached from the entrance");
                    }
                }
            }

            if (t.NpcSpot is (int nx, int ny))
            {
                Assert.False(blocked[(ny * t.Width) + nx], $"{t.Id} (seed {seed}) NPC spot is under furniture");
                Assert.True(reach.Contains((nx, ny)), $"{t.Id} (seed {seed}) NPC spot cannot be reached");
            }
        }
    }

    [Fact]
    public void Generate_Furniture_FitsWhatTheBuildingIsFor()
    {
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            VillageBuildingTemplate house = VillageBuildingGenerator.Generate(VillageBuildingRole.House, seed);
            Assert.True(Count(house, StructureType.WoodBed) >= 1, $"{house.Id} (seed {seed}) has no bed");
            Assert.True(Count(house, StructureType.Stove) == 1, $"{house.Id} (seed {seed}) needs one stove to cook on");
            Assert.Equal(0, Count(house, StructureType.ForgeAnvil));
            Assert.Null(house.NpcSpot);

            VillageBuildingTemplate smithy = VillageBuildingGenerator.Generate(VillageBuildingRole.Smithy, seed);
            Assert.Equal(1, Count(smithy, StructureType.ForgeAnvil));
            Assert.True(smithy.NpcSpot is (int sx, int sy) && NextTo(smithy, StructureType.ForgeAnvil, sx, sy), $"{smithy.Id} (seed {seed}): the smith must stand at the anvil");

            VillageBuildingTemplate store = VillageBuildingGenerator.Generate(VillageBuildingRole.Store, seed);
            Assert.True(Count(store, StructureType.WoodShelf) >= 2, $"{store.Id} (seed {seed}) has fewer than two shelves");
            Assert.Equal(0, Count(store, StructureType.ForgeAnvil));
            Assert.True(store.NpcSpot is (int tx, int ty) && NextTo(store, StructureType.WoodTable, tx, ty), $"{store.Id} (seed {seed}): the trader must stand at the counter");
        }
    }

    [Fact]
    public void Generate_Stall_IsOpenWithACounterAndAMerchant()
    {
        VillageBuildingTemplate stall = VillageBuildingGenerator.Generate(VillageBuildingRole.Stall, 7UL);

        Assert.Equal(0, stall.RoomCount);
        Assert.Equal(1, Count(stall, StructureType.MarketStall));
        Assert.NotNull(stall.NpcSpot);
    }

    internal static int CountRooms(VillageBuildingTemplate t)
    {
        bool[] seen = new bool[t.Width * t.Height];
        int rooms = 0;
        for (int i = 0; i < seen.Length; i++)
        {
            if (seen[i] || t.CellAt(i % t.Width, i / t.Width) != VillageBuildingTemplate.FloorCell)
            {
                continue;
            }

            rooms++;
            Queue<int> queue = new();
            queue.Enqueue(i);
            seen[i] = true;
            while (queue.Count > 0)
            {
                int k = queue.Dequeue();
                foreach ((int dx, int dy) in s_steps)
                {
                    int x = (k % t.Width) + dx;
                    int y = (k / t.Width) + dy;
                    if (t.CellAt(x, y) == VillageBuildingTemplate.FloorCell && !seen[(y * t.Width) + x])
                    {
                        seen[(y * t.Width) + x] = true;
                        queue.Enqueue((y * t.Width) + x);
                    }
                }
            }
        }

        return rooms;
    }

    // An L-shaped building has an empty back corner inside its bounding box.
    private static bool IsLShaped(VillageBuildingTemplate t) =>
        t.CellAt(0, 0) == VillageBuildingTemplate.EmptyCell || t.CellAt(t.Width - 1, 0) == VillageBuildingTemplate.EmptyCell;

    private static int Count(VillageBuildingTemplate t, StructureType type)
    {
        int n = 0;
        foreach (VillageFurniture f in t.Furniture)
        {
            n += f.Type == type ? 1 : 0;
        }

        return n;
    }

    private static bool NextTo(VillageBuildingTemplate t, StructureType type, int x, int y)
    {
        foreach (VillageFurniture f in t.Furniture)
        {
            if (f.Type == type && Math.Abs(f.X - x) + Math.Abs(f.Y - y) == 1)
            {
                return true;
            }
        }

        return false;
    }

    private static bool[] FurnitureMask(VillageBuildingTemplate t, StructureCatalog structures)
    {
        bool[] blocked = new bool[t.Width * t.Height];
        foreach (VillageFurniture f in t.Furniture)
        {
            (int w, int h) = structures.TryGet(f.Type, out StructureDefinition def) ? def.GetFootprint(f.Rotation) : (1, 1);
            for (int y = f.Y; y < f.Y + h; y++)
            {
                for (int x = f.X; x < f.X + w; x++)
                {
                    Assert.Equal(VillageBuildingTemplate.FloorCell, t.CellAt(x, y));
                    Assert.False(blocked[(y * t.Width) + x], $"{t.Id}: two pieces of furniture overlap at {x},{y}");
                    blocked[(y * t.Width) + x] = true;
                }
            }
        }

        return blocked;
    }

    // Cells reachable on foot from the porch in front of the entrance: floor (not under furniture) and doors.
    private static HashSet<(int, int)> Reach(VillageBuildingTemplate t, bool[] blocked)
    {
        HashSet<(int, int)> seen = [];
        Queue<(int, int)> queue = new();
        for (int x = 0; x < t.Width; x++)
        {
            if (t.CellAt(x, t.Height - 1) == VillageBuildingTemplate.PorchCell)
            {
                _ = seen.Add((x, t.Height - 1));
                queue.Enqueue((x, t.Height - 1));
            }
        }

        while (queue.Count > 0)
        {
            (int cx, int cy) = queue.Dequeue();
            foreach ((int dx, int dy) in s_steps)
            {
                int x = cx + dx;
                int y = cy + dy;
                char c = t.CellAt(x, y);
                bool open = c == VillageBuildingTemplate.DoorCell ||
                    (c == VillageBuildingTemplate.FloorCell && !blocked[(y * t.Width) + x]);
                if (open && seen.Add((x, y)))
                {
                    queue.Enqueue((x, y));
                }
            }
        }

        return seen;
    }
}

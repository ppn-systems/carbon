// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Building;
using Carbon.Primitives.Random;

namespace Carbon.Maps.Settlements;

/// <summary>
/// Generates the footprint of a village building from a seed (ADR-027): a rectangle or an L-shape of varied size, one to
/// three rooms split by interior walls with doors, an entrance on the south wall, an apron in front of it (a porch, a
/// shop's paved frontage, or the smithy's open-sided work yard) and furniture chosen by the building's role: a home gets
/// beds, a stove, a table and storage; the smithy its anvil and workbench out in the yard with the smith beside them; the
/// store shelves and a counter; the storehouse rows of shelves and chests. The result is a pure function of
/// <c>(role, seed)</c>, so the server and every client generate the same building. Plan time only.
/// </summary>
public static class VillageBuildingGenerator
{
    private static readonly Lazy<StructureCatalog> s_structures = new(StructureCatalog.LoadEmbedded);

    private static readonly (int Dx, int Dy)[] s_steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>Generates a building.</summary>
    /// <param name="role">What the building is for.</param>
    /// <param name="seed">Seed; the same seed always gives the same building.</param>
    /// <param name="compact">Whether to keep to the smallest sizes (for a crowded site).</param>
    /// <returns>The building footprint.</returns>
    public static VillageBuildingTemplate Generate(VillageBuildingRole role, ulong seed, bool compact = false)
    {
        if (role == VillageBuildingRole.Stall)
        {
            return Stall(seed);
        }

        DeterministicRandom rng = new(seed);
        Builder b = Shape(role, compact, ref rng);
        b.MinEntranceRoom = role == VillageBuildingRole.House ? 8 : 14;
        int rooms = TargetRooms(role, b, ref rng);
        for (int i = 1; i < rooms; i++)
        {
            if (!b.TrySplit(ref rng))
            {
                break;
            }
        }

        b.LabelRooms();
        Furnish(role, b, ref rng);
        string id = $"{role.ToString().ToLowerInvariant()}-{b.Width}x{b.BodyHeight}-{(b.IsL ? "l" : "rect")}-{b.RoomCount}r";
        return new VillageBuildingTemplate(id, role, b.Width, b.Height, b.Cells, b.Furniture.AsReadOnly(), b.Npc, b.RoomCount);
    }

    // An open market stall on the square: a counter, a crate and the merchant behind it. Mirrored by the seed.
    private static VillageBuildingTemplate Stall(ulong seed)
    {
        bool mirror = (seed & 1UL) != 0;
        char[] cells = new string(VillageBuildingTemplate.EmptyCell, 4 * 3).ToCharArray();
        VillageFurniture[] furniture =
        [
            new(StructureType.MarketStall, 1, 1, StructureRotation.North),
            new(StructureType.WoodChest, mirror ? 3 : 0, 0, StructureRotation.North),
        ];
        return new VillageBuildingTemplate("stall", VillageBuildingRole.Stall, 4, 3, cells, furniture, (mirror ? 1 : 2, 0), 0);
    }

    private static Builder Shape(VillageBuildingRole role, bool compact, ref DeterministicRandom rng)
    {
        // Sizes include the walls: a 3x4 bedroom needs a 5x6 shell. Aprons: homes and the storehouse get a porch row,
        // the store a two-row paved frontage, the smithy a three-row work yard with roof pillars at its outer corners.
        (int minW, int maxW, int minH, int maxH, float lChance) = compact ? (6, 7, 5, 6, 0f) : role switch
        {
            VillageBuildingRole.House => (7, 11, 6, 9, 0.4f),
            VillageBuildingRole.Smithy => (8, 10, 6, 7, 0f),
            VillageBuildingRole.Store => (7, 9, 6, 8, 0.3f),
            VillageBuildingRole.Storehouse => (8, 11, 6, 7, 0f),
            VillageBuildingRole.Stall => (6, 9, 6, 7, 0f),
            _ => (6, 9, 6, 7, 0f),
        };
        (int apronRows, char apronCell) = role switch
        {
            VillageBuildingRole.Smithy => (3, VillageBuildingTemplate.YardCell),
            VillageBuildingRole.Store => (2, VillageBuildingTemplate.YardCell),
            VillageBuildingRole.House or VillageBuildingRole.Storehouse or VillageBuildingRole.Stall => (1, VillageBuildingTemplate.PorchCell),
            _ => (1, VillageBuildingTemplate.PorchCell),
        };

        int w = rng.NextInt(minW, maxW + 1);
        int bodyH = rng.NextInt(minH, maxH + 1);
        Builder b = new(w, bodyH, apronRows, apronCell);
        if (role == VillageBuildingRole.Smithy)
        {
            b.SetPillars();
        }

        if (w >= 7 && bodyH >= 6 && rng.NextBool(lChance))
        {
            // Cut a notch out of a back corner; the south (entrance) wall always spans the full width.
            int nw = rng.NextInt(3, w - 3);
            int nh = rng.NextInt(2, bodyH - 3);
            bool left = rng.NextBool();
            b.CutNotch(left ? 0 : w - nw, 0, nw, nh);
        }

        b.BuildWalls();
        b.PlaceEntrance(ref rng);
        return b;
    }

    private static int TargetRooms(VillageBuildingRole role, Builder b, ref DeterministicRandom rng)
    {
        int floor = b.FloorCount;
        int max = role == VillageBuildingRole.Storehouse ? 1 : floor >= 30 ? 3 : floor >= 15 ? 2 : 1;
        float roll = rng.NextFloat();
        int rooms = role == VillageBuildingRole.House
            ? (roll < 0.2f ? 1 : roll < 0.7f ? 2 : 3)
            : (roll < 0.4f ? 1 : 2);
        return Math.Min(rooms, max);
    }

    private static void Furnish(VillageBuildingRole role, Builder b, ref DeterministicRandom rng)
    {
        int main = b.EntranceRoom;
        switch (role)
        {
            case VillageBuildingRole.Smithy:
                // The anvil and the workbench stand out in the open yard, the smith beside the anvil; the forge fire
                // (stove), stock and tools are inside the stone hall.
                if (!b.TryPlaceInApron(StructureType.ForgeAnvil, withNpc: true, ref rng))
                {
                    _ = b.TryPlaceWithNpc(StructureType.ForgeAnvil, main, ref rng);
                }

                _ = b.TryPlaceInApron(StructureType.Workbench, withNpc: false, ref rng);
                _ = b.TryPlace(StructureType.Stove, main, StructureRotation.North, againstWall: true, ref rng);
                _ = b.TryPlace(StructureType.WoodShelf, main, StructureRotation.North, againstWall: true, ref rng);
                _ = b.TryPlace(StructureType.WoodChest, main, StructureRotation.North, againstWall: true, ref rng);
                _ = b.TryPlace(StructureType.WoodChest, main, StructureRotation.North, againstWall: true, ref rng);
                _ = FurnishBackRooms(b, main, ref rng, bedroomFirst: true);
                break;

            case VillageBuildingRole.Storehouse:
                // Shelves and chests along every wall, then free-standing shelves where an aisle still reaches the rest.
                for (int i = 0; i < 14; i++)
                {
                    _ = b.TryPlace(i % 3 == 2 ? StructureType.WoodChest : StructureType.WoodShelf, main, StructureRotation.North, againstWall: true, ref rng);
                }

                for (int i = 0; i < 4; i++)
                {
                    _ = b.TryPlace(StructureType.WoodShelf, main, StructureRotation.North, againstWall: false, ref rng);
                }

                break;

            case VillageBuildingRole.Store:
                // Shelves along the walls, then a counter of two tables (a single table in a cramped shop) with the
                // trader behind it, then more shelves where there is room.
                _ = b.TryPlace(StructureType.WoodShelf, main, StructureRotation.North, againstWall: true, ref rng);
                _ = b.TryPlace(StructureType.WoodShelf, main, StructureRotation.North, againstWall: true, ref rng);
                if (!b.TryPlaceCounter(main, ref rng))
                {
                    _ = b.TryPlaceWithNpc(StructureType.WoodTable, main, ref rng);
                }

                _ = b.TryPlace(StructureType.WoodShelf, main, StructureRotation.North, againstWall: true, ref rng);
                _ = b.TryPlace(StructureType.WoodShelf, main, StructureRotation.North, againstWall: true, ref rng);
                _ = b.TryPlace(StructureType.WoodChest, main, StructureRotation.North, againstWall: true, ref rng);
                _ = FurnishBackRooms(b, main, ref rng, bedroomFirst: false);
                break;

            case VillageBuildingRole.House:
            case VillageBuildingRole.Stall:
            default:
                bool bed = FurnishBackRooms(b, main, ref rng, bedroomFirst: true);
                if (!bed || b.RoomCount == 1)
                {
                    _ = b.TryPlaceBed(main, ref rng);
                }

                if (b.RoomCount == 1 && b.FloorCount >= 20)
                {
                    _ = b.TryPlaceBed(main, ref rng);
                }

                _ = b.TryPlace(StructureType.Stove, main, StructureRotation.North, againstWall: true, ref rng);
                _ = b.TryPlace(StructureType.WoodTable, main, StructureRotation.North, againstWall: false, ref rng);
                _ = b.TryPlace(rng.NextBool() ? StructureType.WoodShelf : StructureType.WoodChest, main, StructureRotation.North, againstWall: true, ref rng);
                break;
        }
    }

    // Returns whether a bed was placed.
    private static bool FurnishBackRooms(Builder b, int main, ref DeterministicRandom rng, bool bedroomFirst)
    {
        bool bed = false;
        int order = 0;
        for (int room = 0; room < b.RoomCount; room++)
        {
            if (room == main)
            {
                continue;
            }

            bool bedroom = order == 0 ? bedroomFirst : rng.NextBool(0.5f);
            order++;
            if (bedroom)
            {
                bed |= b.TryPlaceBed(room, ref rng);
                if (b.RoomSize(room) >= 9)
                {
                    bed |= b.TryPlaceBed(room, ref rng);
                }

                _ = b.TryPlace(StructureType.WoodChest, room, StructureRotation.North, againstWall: true, ref rng);
            }
            else
            {
                // A storeroom or a small workroom.
                if (rng.NextBool(0.4f))
                {
                    _ = b.TryPlace(StructureType.Workbench, room, StructureRotation.North, againstWall: true, ref rng);
                }

                _ = b.TryPlace(StructureType.WoodChest, room, StructureRotation.North, againstWall: true, ref rng);
                _ = b.TryPlace(StructureType.WoodShelf, room, StructureRotation.North, againstWall: true, ref rng);
                _ = b.TryPlace(StructureType.WoodChest, room, StructureRotation.North, againstWall: true, ref rng);
            }
        }

        return bed;
    }

    private static (int W, int H) FootprintOf(StructureType type, StructureRotation rotation) =>
        s_structures.Value.TryGet(type, out StructureDefinition def) ? def.GetFootprint(rotation) : (1, 1);

    /// <summary>Mutable grid the generator works on. The body is rows 0..BodyHeight-1; the last row is the porch.</summary>
    private sealed class Builder
    {
        private readonly bool[] _inside;
        private readonly bool[] _blocked;
        private readonly bool[] _reserved;
        private int[] _room = [];
        private int _doorX;

        public Builder(int width, int bodyHeight, int apronRows, char apronCell)
        {
            this.Width = width;
            this.BodyHeight = bodyHeight;
            this.Height = bodyHeight + apronRows;
            this.Cells = new char[width * this.Height];
            _inside = new bool[width * this.Height];
            _blocked = new bool[width * this.Height];
            _reserved = new bool[width * this.Height];
            Array.Fill(this.Cells, VillageBuildingTemplate.EmptyCell);
            for (int y = 0; y < bodyHeight; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    _inside[(y * width) + x] = true;
                }
            }

            for (int y = bodyHeight; y < this.Height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    this.Cells[(y * width) + x] = apronCell;
                }
            }
        }

        public int Width { get; }

        public int Height { get; }

        public int BodyHeight { get; }

        public char[] Cells { get; }

        public bool IsL { get; private set; }

        public List<VillageFurniture> Furniture { get; } = [];

        public (int X, int Y)? Npc { get; private set; }

        public int RoomCount { get; private set; }

        public int EntranceRoom { get; private set; }

        // Floor cells the room behind the entrance keeps when the building is split (a shop needs room for its counter).
        public int MinEntranceRoom { get; set; }

        public int FloorCount
        {
            get
            {
                int n = 0;
                foreach (char c in this.Cells)
                {
                    n += c == VillageBuildingTemplate.FloorCell ? 1 : 0;
                }

                return n;
            }
        }

        private (int X, int Y) InsideEntrance => (_doorX, this.BodyHeight - 2);

        // Roof pillars at the outer corners of the work yard (an open-sided shelter, like a RimWorld forge yard).
        public void SetPillars()
        {
            this.Set(0, this.Height - 1, VillageBuildingTemplate.PillarCell);
            this.Set(this.Width - 1, this.Height - 1, VillageBuildingTemplate.PillarCell);
        }

        public void CutNotch(int x0, int y0, int w, int h)
        {
            for (int y = y0; y < y0 + h; y++)
            {
                for (int x = x0; x < x0 + w; x++)
                {
                    _inside[(y * this.Width) + x] = false;
                }
            }

            this.IsL = true;
        }

        public void BuildWalls()
        {
            for (int y = 0; y < this.BodyHeight; y++)
            {
                for (int x = 0; x < this.Width; x++)
                {
                    if (!this.Inside(x, y))
                    {
                        continue;
                    }

                    bool edge = false;
                    for (int dy = -1; dy <= 1 && !edge; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (!this.Inside(x + dx, y + dy))
                            {
                                edge = true;
                                break;
                            }
                        }
                    }

                    this.Set(x, y, edge ? VillageBuildingTemplate.WallCell : VillageBuildingTemplate.FloorCell);
                }
            }
        }

        public void PlaceEntrance(ref DeterministicRandom rng)
        {
            int y = this.BodyHeight - 1;
            List<int> options = [];
            for (int x = 1; x < this.Width - 1; x++)
            {
                if (this.At(x, y - 1) == VillageBuildingTemplate.FloorCell)
                {
                    options.Add(x);
                }
            }

            _doorX = options[rng.NextInt(0, options.Count)];
            this.Set(_doorX, y, VillageBuildingTemplate.DoorCell);

            // The way out stays clear: nothing is placed on the apron in front of the door.
            for (int ay = this.BodyHeight; ay < this.Height; ay++)
            {
                _reserved[this.Index(_doorX, ay)] = true;
            }
        }

        // Turns one straight run of floor into an interior wall with a door, if both sides stay proper rooms.
        public bool TrySplit(ref DeterministicRandom rng)
        {
            List<(int X, int Y, bool Vertical, int Length)> runs = [];
            for (int x = 2; x < this.Width - 2; x++)
            {
                this.CollectRuns(x, vertical: true, runs);
            }

            for (int y = 2; y < this.BodyHeight - 2; y++)
            {
                this.CollectRuns(y, vertical: false, runs);
            }

            Shuffle(runs, ref rng);
            foreach ((int sx, int sy, bool vertical, int length) in runs)
            {
                if (this.TryApplySplit(sx, sy, vertical, length, ref rng))
                {
                    return true;
                }
            }

            return false;
        }

        public void LabelRooms()
        {
            _room = new int[this.Cells.Length];
            Array.Fill(_room, -1);
            int rooms = 0;
            for (int i = 0; i < this.Cells.Length; i++)
            {
                if (this.Cells[i] == VillageBuildingTemplate.FloorCell && _room[i] < 0)
                {
                    this.Flood(i % this.Width, i / this.Width, rooms++);
                }
            }

            this.RoomCount = rooms;
            this.EntranceRoom = _room[this.Index(this.InsideEntrance.X, this.InsideEntrance.Y)];

            // Keep every doorway clear: the floor on both sides of each door is reserved.
            for (int i = 0; i < this.Cells.Length; i++)
            {
                if (this.Cells[i] != VillageBuildingTemplate.DoorCell)
                {
                    continue;
                }

                foreach ((int dx, int dy) in s_steps)
                {
                    int nx = (i % this.Width) + dx;
                    int ny = (i / this.Width) + dy;
                    if (this.At(nx, ny) == VillageBuildingTemplate.FloorCell)
                    {
                        _reserved[this.Index(nx, ny)] = true;
                    }
                }
            }
        }

        public int RoomSize(int room)
        {
            int n = 0;
            foreach (int r in _room)
            {
                n += r == room ? 1 : 0;
            }

            return n;
        }

        public bool TryPlaceBed(int room, ref DeterministicRandom rng) =>
            this.TryPlace(StructureType.WoodBed, room, StructureRotation.North, againstWall: true, ref rng) ||
            this.TryPlace(StructureType.WoodBed, room, StructureRotation.East, againstWall: true, ref rng);

        public bool TryPlace(StructureType type, int room, StructureRotation rotation, bool againstWall, ref DeterministicRandom rng)
        {
            (int fw, int fh) = FootprintOf(type, rotation);
            List<(int X, int Y)> anchors = this.Anchors(room, fw, fh, againstWall);
            Shuffle(anchors, ref rng);
            foreach ((int ax, int ay) in anchors)
            {
                if (this.TryCommit(type, ax, ay, fw, fh, rotation))
                {
                    return true;
                }
            }

            return false;
        }

        // A station with a free tile beside it where the building's NPC stands (the smith at the anvil).
        public bool TryPlaceWithNpc(StructureType type, int room, ref DeterministicRandom rng)
        {
            List<(int X, int Y)> anchors = this.Anchors(room, 1, 1, againstWall: false);
            Shuffle(anchors, ref rng);
            foreach ((int ax, int ay) in anchors)
            {
                foreach ((int dx, int dy) in s_steps)
                {
                    int nx = ax + dx;
                    int ny = ay + dy;
                    if (!this.IsFreeFloor(nx, ny, room) || !this.TryCommit(type, ax, ay, 1, 1, StructureRotation.North))
                    {
                        continue;
                    }

                    this.Npc = (nx, ny);
                    _reserved[this.Index(nx, ny)] = true;
                    return true;
                }
            }

            return false;
        }

        // Furniture out on the apron (the smithy's yard), optionally with the NPC standing beside it.
        public bool TryPlaceInApron(StructureType type, bool withNpc, ref DeterministicRandom rng)
        {
            (int fw, int fh) = FootprintOf(type, StructureRotation.North);
            List<(int X, int Y)> anchors = [];
            for (int y = this.BodyHeight; y + fh <= this.Height; y++)
            {
                for (int x = 0; x + fw <= this.Width; x++)
                {
                    if (this.IsFreeApron(x, y, fw, fh))
                    {
                        anchors.Add((x, y));
                    }
                }
            }

            Shuffle(anchors, ref rng);
            foreach ((int ax, int ay) in anchors)
            {
                (int X, int Y)? npc = null;
                if (withNpc)
                {
                    foreach ((int dx, int dy) in s_steps)
                    {
                        if (fw == 1 && fh == 1 && this.IsFreeApron(ax + dx, ay + dy, 1, 1))
                        {
                            npc = (ax + dx, ay + dy);
                            break;
                        }
                    }

                    if (npc is null)
                    {
                        continue;
                    }
                }

                for (int j = 0; j < fh; j++)
                {
                    for (int i = 0; i < fw; i++)
                    {
                        _blocked[this.Index(ax + i, ay + j)] = true;
                    }
                }

                // Every free yard tile (and so the smith's spot) must still be reachable from the yard's open edge.
                if (!this.EveryFreeApronReachable())
                {
                    for (int j = 0; j < fh; j++)
                    {
                        for (int i = 0; i < fw; i++)
                        {
                            _blocked[this.Index(ax + i, ay + j)] = false;
                        }
                    }

                    continue;
                }

                this.Furniture.Add(new VillageFurniture(type, ax, ay, StructureRotation.North));
                if (npc is (int nx, int ny))
                {
                    this.Npc = (nx, ny);
                    _reserved[this.Index(nx, ny)] = true;
                }

                return true;
            }

            return false;
        }

        // Apron tiles reachable on foot from outside: the open bottom row and the open sides of the apron.
        private bool EveryFreeApronReachable()
        {
            bool[] seen = new bool[this.Cells.Length];
            Queue<int> queue = new();
            for (int y = this.BodyHeight; y < this.Height; y++)
            {
                for (int x = 0; x < this.Width; x++)
                {
                    bool edge = y == this.Height - 1 || x == 0 || x == this.Width - 1;
                    int k = this.Index(x, y);
                    if (edge && this.IsOpenApron(k))
                    {
                        seen[k] = true;
                        queue.Enqueue(k);
                    }
                }
            }

            while (queue.Count > 0)
            {
                int k = queue.Dequeue();
                foreach ((int dx, int dy) in s_steps)
                {
                    int nx = (k % this.Width) + dx;
                    int ny = (k / this.Width) + dy;
                    if (nx < 0 || nx >= this.Width || ny < this.BodyHeight || ny >= this.Height)
                    {
                        continue;
                    }

                    int n = this.Index(nx, ny);
                    if (!seen[n] && this.IsOpenApron(n))
                    {
                        seen[n] = true;
                        queue.Enqueue(n);
                    }
                }
            }

            for (int k = this.BodyHeight * this.Width; k < this.Cells.Length; k++)
            {
                if (this.IsOpenApron(k) && !seen[k])
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsOpenApron(int k) =>
            this.Cells[k] is VillageBuildingTemplate.YardCell or VillageBuildingTemplate.PorchCell && !_blocked[k];

        private bool IsFreeApron(int x, int y, int fw, int fh)
        {
            for (int j = 0; j < fh; j++)
            {
                for (int i = 0; i < fw; i++)
                {
                    int cx = x + i;
                    int cy = y + j;
                    if (cx < 0 || cx >= this.Width || cy < this.BodyHeight || cy >= this.Height)
                    {
                        return false;
                    }

                    int k = this.Index(cx, cy);
                    if (this.Cells[k] != VillageBuildingTemplate.YardCell || _blocked[k] || _reserved[k])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // A shop counter (two tables side by side) with the shopkeeper behind it, on the side away from the door.
        public bool TryPlaceCounter(int room, ref DeterministicRandom rng)
        {
            List<(int X, int Y)> anchors = this.Anchors(room, 2, 1, againstWall: false);
            Shuffle(anchors, ref rng);
            foreach ((int ax, int ay) in anchors)
            {
                int ny = ay - 1;
                int nx = ax + (rng.NextBool() ? 0 : 1);
                if (!this.IsFreeFloor(nx, ny, room) || !this.IsFreeFloor(ax, ay + 1, room) || !this.IsFreeFloor(ax + 1, ay + 1, room))
                {
                    continue;
                }

                if (!this.TryCommit(StructureType.WoodTable, ax, ay, 1, 1, StructureRotation.North))
                {
                    continue;
                }

                if (!this.TryCommit(StructureType.WoodTable, ax + 1, ay, 1, 1, StructureRotation.North))
                {
                    this.Uncommit(ax, ay, 1, 1);
                    continue;
                }

                if (!this.Reachable(nx, ny))
                {
                    this.Uncommit(ax, ay, 2, 1);
                    continue;
                }

                this.Npc = (nx, ny);
                _reserved[this.Index(nx, ny)] = true;
                return true;
            }

            return false;
        }

        private void CollectRuns(int line, bool vertical, List<(int X, int Y, bool Vertical, int Length)> runs)
        {
            int length = vertical ? this.BodyHeight : this.Width;
            int start = -1;
            for (int i = 0; i <= length; i++)
            {
                (int x, int y) = vertical ? (line, i) : (i, line);
                bool floor = i < length && this.At(x, y) == VillageBuildingTemplate.FloorCell;
                if (floor && start < 0)
                {
                    start = i;
                }
                else if (!floor && start >= 0)
                {
                    (int sx, int sy) = vertical ? (line, start) : (start, line);
                    runs.Add((sx, sy, vertical, i - start));
                    start = -1;
                }
            }
        }

        private bool TryApplySplit(int sx, int sy, bool vertical, int length, ref DeterministicRandom rng)
        {
            (int dx, int dy) = vertical ? (0, 1) : (1, 0);
            (int px, int py) = vertical ? (1, 0) : (0, 1);
            if (length < 2)
            {
                return false;
            }

            // The run must end on walls at both ends and must not wall off the entrance.
            for (int i = 0; i < length; i++)
            {
                int x = sx + (dx * i);
                int y = sy + (dy * i);
                if ((x, y) == this.InsideEntrance || this.IsNextToDoor(x, y))
                {
                    return false;
                }
            }

            // The new wall needs two tiles of floor on both sides along its whole length, and three on one side: a room is
            // at least 2 wide (a kitchen or pantry) and the room beside it at least 3 (a bedroom).
            for (int i = 0; i < length; i++)
            {
                int x = sx + (dx * i);
                int y = sy + (dy * i);
                bool wideBefore = this.At(x - (3 * px), y - (3 * py)) == VillageBuildingTemplate.FloorCell;
                bool wideAfter = this.At(x + (3 * px), y + (3 * py)) == VillageBuildingTemplate.FloorCell;
                if (this.At(x - px, y - py) != VillageBuildingTemplate.FloorCell || this.At(x + px, y + py) != VillageBuildingTemplate.FloorCell ||
                    this.At(x - (2 * px), y - (2 * py)) != VillageBuildingTemplate.FloorCell || this.At(x + (2 * px), y + (2 * py)) != VillageBuildingTemplate.FloorCell ||
                    !(wideBefore || wideAfter))
                {
                    return false;
                }
            }

            for (int i = 0; i < length; i++)
            {
                this.Set(sx + (dx * i), sy + (dy * i), VillageBuildingTemplate.WallCell);
            }

            if (this.FloorReachableWithoutDoors(this.InsideEntrance.X, this.InsideEntrance.Y) < this.MinEntranceRoom)
            {
                for (int i = 0; i < length; i++)
                {
                    this.Set(sx + (dx * i), sy + (dy * i), VillageBuildingTemplate.FloorCell);
                }

                return false;
            }

            int door = rng.NextInt(0, length);
            this.Set(sx + (dx * door), sy + (dy * door), VillageBuildingTemplate.DoorCell);
            return true;
        }

        private int FloorReachableWithoutDoors(int x, int y)
        {
            bool[] seen = new bool[this.Cells.Length];
            Queue<(int X, int Y)> queue = new();
            seen[this.Index(x, y)] = true;
            queue.Enqueue((x, y));
            int count = 0;
            while (queue.Count > 0)
            {
                (int cx, int cy) = queue.Dequeue();
                count++;
                foreach ((int ddx, int ddy) in s_steps)
                {
                    int nx = cx + ddx;
                    int ny = cy + ddy;
                    if (this.At(nx, ny) == VillageBuildingTemplate.FloorCell && !seen[this.Index(nx, ny)])
                    {
                        seen[this.Index(nx, ny)] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }

            return count;
        }

        private bool IsNextToDoor(int x, int y)
        {
            foreach ((int dx, int dy) in s_steps)
            {
                if (this.At(x + dx, y + dy) == VillageBuildingTemplate.DoorCell)
                {
                    return true;
                }
            }

            return false;
        }

        private List<(int X, int Y)> Anchors(int room, int fw, int fh, bool againstWall)
        {
            List<(int X, int Y)> anchors = [];
            for (int y = 0; y < this.BodyHeight; y++)
            {
                for (int x = 0; x < this.Width; x++)
                {
                    bool fits = true;
                    bool touchesWall = false;
                    for (int j = 0; j < fh && fits; j++)
                    {
                        for (int i = 0; i < fw; i++)
                        {
                            if (!this.IsFreeFloor(x + i, y + j, room))
                            {
                                fits = false;
                                break;
                            }

                            touchesWall |= this.TouchesWall(x + i, y + j);
                        }
                    }

                    if (fits && (!againstWall || touchesWall))
                    {
                        anchors.Add((x, y));
                    }
                }
            }

            return anchors;
        }

        private bool TouchesWall(int x, int y)
        {
            foreach ((int dx, int dy) in s_steps)
            {
                if (this.At(x + dx, y + dy) == VillageBuildingTemplate.WallCell)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryCommit(StructureType type, int ax, int ay, int fw, int fh, StructureRotation rotation)
        {
            for (int j = 0; j < fh; j++)
            {
                for (int i = 0; i < fw; i++)
                {
                    _blocked[this.Index(ax + i, ay + j)] = true;
                }
            }

            if (!this.EveryFreeFloorReachable())
            {
                this.Uncommit(ax, ay, fw, fh);
                return false;
            }

            this.Furniture.Add(new VillageFurniture(type, ax, ay, rotation));
            return true;
        }

        private void Uncommit(int ax, int ay, int fw, int fh)
        {
            for (int j = 0; j < fh; j++)
            {
                for (int i = 0; i < fw; i++)
                {
                    _blocked[this.Index(ax + i, ay + j)] = false;
                }
            }

            for (int k = this.Furniture.Count - 1; k >= 0; k--)
            {
                VillageFurniture f = this.Furniture[k];
                if (f.X >= ax && f.X < ax + fw && f.Y >= ay && f.Y < ay + fh)
                {
                    this.Furniture.RemoveAt(k);
                }
            }
        }

        private bool Reachable(int x, int y) => this.ReachableSet()[this.Index(x, y)];

        private bool EveryFreeFloorReachable()
        {
            bool[] seen = this.ReachableSet();
            for (int i = 0; i < this.Cells.Length; i++)
            {
                if (this.Cells[i] == VillageBuildingTemplate.FloorCell && !_blocked[i] && !seen[i])
                {
                    return false;
                }
            }

            return true;
        }

        // Floor and doors reachable on foot from the tile inside the entrance.
        private bool[] ReachableSet()
        {
            bool[] seen = new bool[this.Cells.Length];
            Queue<int> queue = new();
            int start = this.Index(this.InsideEntrance.X, this.InsideEntrance.Y);
            if (_blocked[start])
            {
                return seen;
            }

            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                foreach ((int dx, int dy) in s_steps)
                {
                    int nx = (i % this.Width) + dx;
                    int ny = (i / this.Width) + dy;
                    if (nx < 0 || ny < 0 || nx >= this.Width || ny >= this.BodyHeight)
                    {
                        continue;
                    }

                    int n = this.Index(nx, ny);
                    char c = this.Cells[n];
                    if (seen[n] || _blocked[n] || c is not (VillageBuildingTemplate.FloorCell or VillageBuildingTemplate.DoorCell) ||
                        (c == VillageBuildingTemplate.DoorCell && ny == this.BodyHeight - 1))
                    {
                        continue;
                    }

                    seen[n] = true;
                    queue.Enqueue(n);
                }
            }

            return seen;
        }

        private bool IsFreeFloor(int x, int y, int room)
        {
            if (x < 0 || y < 0 || x >= this.Width || y >= this.BodyHeight)
            {
                return false;
            }

            int i = this.Index(x, y);
            return this.Cells[i] == VillageBuildingTemplate.FloorCell && !_blocked[i] && !_reserved[i] && _room[i] == room;
        }

        private void Flood(int x, int y, int room)
        {
            Queue<(int X, int Y)> queue = new();
            _room[this.Index(x, y)] = room;
            queue.Enqueue((x, y));
            while (queue.Count > 0)
            {
                (int cx, int cy) = queue.Dequeue();
                foreach ((int dx, int dy) in s_steps)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (this.At(nx, ny) == VillageBuildingTemplate.FloorCell && _room[this.Index(nx, ny)] < 0)
                    {
                        _room[this.Index(nx, ny)] = room;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
        }

        private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < this.Width && y < this.BodyHeight && _inside[(y * this.Width) + x];

        private char At(int x, int y) =>
            x >= 0 && y >= 0 && x < this.Width && y < this.Height ? this.Cells[(y * this.Width) + x] : VillageBuildingTemplate.EmptyCell;

        private void Set(int x, int y, char c) => this.Cells[(y * this.Width) + x] = c;

        private int Index(int x, int y) => (y * this.Width) + x;

        private static void Shuffle<T>(List<T> list, ref DeterministicRandom rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}

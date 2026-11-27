// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Rendering.Atmospherics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Maps.Building;

namespace Carbon.Desktop.Rendering.World;

/// <summary>
/// Draws roofs over enclosed rooms of the building grid, for village houses and player builds alike (ADR-025). Rooms are
/// found from their walls, cached until <see cref="BuildingGrid.Revision"/> changes, and roofed in the style of their
/// walls: timber shingles, swamp thatch, tundra slate under snow, or a flat desert roof with a parapet. Rooms that share a
/// wall form one building. RimWorld-style cutaway (ADR-027 revision 3): the whole roof of the building the local player
/// stands in is lifted off, and the roofs of buildings near the player fade with distance, so interiors show when you are
/// inside or next to them. Stoves get a chimney; a forge or a row of shelves gets a shop sign. Steady-state frames do not
/// allocate.
/// </summary>
internal sealed class RoofRenderer
{
    /// <summary>Largest interior (tiles) treated as a room; bigger enclosures are courtyards and stay open.</summary>
    public const int MaxRoomTiles = 120;

    /// <summary>UI: distance in tiles from a building's walls beyond which its roof is fully opaque.</summary>
    internal const float FadeStartTiles = 3.5f;

    /// <summary>UI: opacity of a roof seen from right outside its walls (porch, yard, doorstep).</summary>
    internal const float NearAlpha = 0.3f;

    private const int ScanMargin = 12;

    private static readonly (int X, int Y)[] s_cardinal = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private readonly float _tileSize;
    private readonly List<RoofRoom> _rooms = new(64);
    private readonly List<int> _buildingOf = new(64);
    private readonly List<(int MinX, int MinY, int MaxX, int MaxY)> _buildings = new(32);
    private readonly Queue<int> _roomQueue = new(16);
    private readonly List<(int X, int Y)> _roomTiles = new(1024);
    private readonly Dictionary<(int X, int Y), int> _tileToRoom = new(1024);
    private readonly Queue<(int X, int Y)> _queue = new(MaxRoomTiles * 2);
    private readonly HashSet<(int X, int Y)> _visited = new(MaxRoomTiles * 2);
    private readonly Vector2[] _poly = new Vector2[8];
    private int _cachedRevision = -1;
    private (int MinX, int MinY, int MaxX, int MaxY) _scanned;

    /// <summary>Initializes a new instance of the <see cref="RoofRenderer"/> class.</summary>
    /// <param name="tileSize">Tile edge in world pixels.</param>
    public RoofRenderer(float tileSize) => _tileSize = tileSize;

    /// <summary>Gets the room (in world pixels) the position stands in, from the last refresh.</summary>
    public bool TryGetRoomAt(Vector2 worldPos, out RectangleF room)
    {
        int tx = (int)MathF.Floor(worldPos.X / _tileSize);
        int ty = (int)MathF.Floor(worldPos.Y / _tileSize);
        if (_tileToRoom.TryGetValue((tx, ty), out int index))
        {
            RoofRoom r = _rooms[index];
            room = new RectangleF(r.MinX * _tileSize, r.MinY * _tileSize, (r.MaxX - r.MinX + 1) * _tileSize, (r.MaxY - r.MinY + 1) * _tileSize);
            return true;
        }

        room = default;
        return false;
    }

    /// <summary>Re-detects rooms when the grid changed or the view left the scanned area.</summary>
    public void Refresh(BuildingGrid grid, float minX, float minY, float maxX, float maxY)
    {
        ArgumentNullException.ThrowIfNull(grid);
        int vMinX = (int)MathF.Floor(minX / _tileSize);
        int vMinY = (int)MathF.Floor(minY / _tileSize);
        int vMaxX = (int)MathF.Floor(maxX / _tileSize);
        int vMaxY = (int)MathF.Floor(maxY / _tileSize);
        bool inside = vMinX >= _scanned.MinX && vMinY >= _scanned.MinY && vMaxX <= _scanned.MaxX && vMaxY <= _scanned.MaxY;
        if (grid.Revision == _cachedRevision && inside)
        {
            return;
        }

        _cachedRevision = grid.Revision;
        _scanned = (vMinX - ScanMargin, vMinY - ScanMargin, vMaxX + ScanMargin, vMaxY + ScanMargin);
        _rooms.Clear();
        _roomTiles.Clear();
        _tileToRoom.Clear();

        foreach (KeyValuePair<(int X, int Y), StructureTile> kvp in grid.PlacedStructures)
        {
            (int wx, int wy) = kvp.Key;
            if (wx < _scanned.MinX || wx > _scanned.MaxX || wy < _scanned.MinY || wy > _scanned.MaxY ||
                kvp.Value.IsDestroyed || kvp.Value.Category is not (StructureCategory.Wall or StructureCategory.Door))
            {
                continue;
            }

            for (int i = 0; i < s_cardinal.Length; i++)
            {
                int nx = wx + s_cardinal[i].X;
                int ny = wy + s_cardinal[i].Y;
                if (!_tileToRoom.ContainsKey((nx, ny)) && !grid.IsRoomBoundaryAt(nx, ny))
                {
                    this.TryDetectRoom(grid, nx, ny);
                }
            }
        }

        this.GroupBuildings();
    }

    /// <summary>
    /// Gets the opacity of the roof over a tile, as <see cref="Draw"/> draws it, for a player at a position: 0 inside that
    /// building, fading in from <see cref="NearAlpha"/> at its walls to 1 at <see cref="FadeStartTiles"/>. 1 for a tile
    /// under no roof.
    /// </summary>
    /// <param name="tileX">World tile X (inside a room).</param>
    /// <param name="tileY">World tile Y.</param>
    /// <param name="playerWorld">Local player position in world pixels.</param>
    /// <returns>The roof opacity, 0..1.</returns>
    public float RoofAlphaAt(int tileX, int tileY, Vector2 playerWorld) =>
        _tileToRoom.TryGetValue((tileX, tileY), out int room)
            ? this.BuildingAlpha(_buildingOf[room], playerWorld, this.PlayerBuilding(playerWorld))
            : 1f;

    /// <summary>The roof opacity for a player outside a building at a distance (tiles) from its walls.</summary>
    /// <param name="inside">Whether the player stands in a room of the building.</param>
    /// <param name="distanceTiles">Distance from the player to the building's outline, in tiles.</param>
    /// <returns>0 inside; <see cref="NearAlpha"/> at the walls rising linearly to 1 at <see cref="FadeStartTiles"/>.</returns>
    internal static float FadeFor(bool inside, float distanceTiles) =>
        inside ? 0f : NearAlpha + ((1f - NearAlpha) * Math.Clamp(distanceTiles / FadeStartTiles, 0f, 1f));

    private int PlayerBuilding(Vector2 playerWorld)
    {
        int ptx = (int)MathF.Floor(playerWorld.X / _tileSize);
        int pty = (int)MathF.Floor(playerWorld.Y / _tileSize);
        return _tileToRoom.TryGetValue((ptx, pty), out int room) ? _buildingOf[room] : -1;
    }

    private float BuildingAlpha(int building, Vector2 playerWorld, int playerBuilding)
    {
        (int minX, int minY, int maxX, int maxY) = _buildings[building];
        float px = playerWorld.X / _tileSize;
        float py = playerWorld.Y / _tileSize;
        float dx = MathF.Max(MathF.Max(minX - px, px - (maxX + 1)), 0f);
        float dy = MathF.Max(MathF.Max(minY - py, py - (maxY + 1)), 0f);
        return FadeFor(building == playerBuilding, MathF.Sqrt((dx * dx) + (dy * dy)));
    }

    // Rooms whose outlines (interior plus walls) touch share a wall or a door: they are one building and fade together.
    private void GroupBuildings()
    {
        _buildingOf.Clear();
        _buildings.Clear();
        for (int i = 0; i < _rooms.Count; i++)
        {
            _buildingOf.Add(-1);
        }

        for (int i = 0; i < _rooms.Count; i++)
        {
            if (_buildingOf[i] >= 0)
            {
                continue;
            }

            int building = _buildings.Count;
            (int MinX, int MinY, int MaxX, int MaxY) box = (_rooms[i].MinX, _rooms[i].MinY, _rooms[i].MaxX, _rooms[i].MaxY);
            _buildingOf[i] = building;
            _roomQueue.Clear();
            _roomQueue.Enqueue(i);
            while (_roomQueue.Count > 0)
            {
                RoofRoom a = _rooms[_roomQueue.Dequeue()];
                for (int j = 0; j < _rooms.Count; j++)
                {
                    RoofRoom b = _rooms[j];
                    if (_buildingOf[j] < 0 && a.MinX <= b.MaxX && a.MaxX >= b.MinX && a.MinY <= b.MaxY && a.MaxY >= b.MinY)
                    {
                        _buildingOf[j] = building;
                        box = (Math.Min(box.MinX, b.MinX), Math.Min(box.MinY, b.MinY), Math.Max(box.MaxX, b.MaxX), Math.Max(box.MaxY, b.MaxY));
                        _roomQueue.Enqueue(j);
                    }
                }
            }

            _buildings.Add(box);
        }
    }

    /// <summary>Draws every visible roof, cut away over the player's building and faded near it (<see cref="RoofAlphaAt"/>).</summary>
    public void Draw(GeometryBatch batch, BuildingGrid grid, Vector2 playerWorld, float minX, float minY, float maxX, float maxY, float time)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(grid);
        int playerBuilding = this.PlayerBuilding(playerWorld);
        for (int i = 0; i < _rooms.Count; i++)
        {
            RoofRoom room = _rooms[i];
            float rx0 = room.MinX * _tileSize;
            float ry0 = room.MinY * _tileSize;
            float rx1 = (room.MaxX + 1) * _tileSize;
            float ry1 = (room.MaxY + 1) * _tileSize;
            if (rx1 < minX || rx0 > maxX || ry1 < minY - _tileSize || ry0 > maxY)
            {
                continue;
            }

            float alpha = this.BuildingAlpha(_buildingOf[i], playerWorld, playerBuilding);
            if (alpha <= 0.01f)
            {
                continue;
            }

            if (room.IsRectangular)
            {
                this.DrawRectRoof(batch, in room, rx0, ry0, rx1, ry1, time, alpha);
            }
            else
            {
                this.DrawTileRoof(batch, grid, in room, alpha);
            }
        }
    }

    /// <summary>
    /// Adds the night lights of the rooms in view (ADR-041): warm light spilling from each door, a soft window glow
    /// over the room, and the hearth or forge fire at its stove. Uses the rooms of the last <see cref="Refresh"/>.
    /// </summary>
    /// <param name="lights">Light list the frame collects into.</param>
    /// <param name="minX">View left edge.</param>
    /// <param name="minY">View top edge.</param>
    /// <param name="maxX">View right edge.</param>
    /// <param name="maxY">View bottom edge.</param>
    /// <param name="time">Animation time in seconds (fire flicker).</param>
    public void CollectLights(List<PointLight> lights, float minX, float minY, float maxX, float maxY, float time)
    {
        ArgumentNullException.ThrowIfNull(lights);
        ColorRgba warm = new(255, 186, 104);
        ColorRgba fire = new(255, 140, 60);
        for (int i = 0; i < _rooms.Count; i++)
        {
            RoofRoom room = _rooms[i];
            float rx0 = room.MinX * _tileSize;
            float ry0 = room.MinY * _tileSize;
            float rx1 = (room.MaxX + 1) * _tileSize;
            float ry1 = (room.MaxY + 1) * _tileSize;
            const float Reach = 160f;
            if (rx1 + Reach < minX || rx0 - Reach > maxX || ry1 + Reach < minY || ry0 - Reach > maxY)
            {
                continue;
            }

            Vector2 centre = new((rx0 + rx1) * 0.5f, (ry0 + ry1) * 0.5f);
            float span = MathF.Max(rx1 - rx0, ry1 - ry0);
            lights.Add(new PointLight(centre, span * 0.75f, warm, 0.3f));
            if (room.DoorX != int.MinValue)
            {
                Vector2 door = new((room.DoorX + 0.5f) * _tileSize, (room.DoorY + 0.5f) * _tileSize);
                lights.Add(new PointLight(door + (Vector2.Normalize(door - centre + new Vector2(0f, 0.01f)) * _tileSize * 0.8f), 72f, warm, 0.75f));
            }

            if (room.StoveX != int.MinValue)
            {
                Vector2 stove = new((room.StoveX + 0.5f) * _tileSize, (room.StoveY + 0.5f) * _tileSize);
                float flicker = 0.82f + (0.12f * MathF.Sin((time * 9f) + stove.X)) + (0.06f * MathF.Sin((time * 23f) + stove.Y));
                bool forge = room.Emblem == RoofEmblem.Anvil;
                lights.Add(new PointLight(stove, forge ? 130f : 84f, forge ? fire : warm, (forge ? 0.95f : 0.7f) * flicker));
            }
        }
    }

    private void TryDetectRoom(BuildingGrid grid, int startX, int startY)
    {
        _queue.Clear();
        _visited.Clear();
        _queue.Enqueue((startX, startY));
        _ = _visited.Add((startX, startY));
        int tileStart = _roomTiles.Count;
        int minX = startX;
        int maxX = startX;
        int minY = startY;
        int maxY = startY;
        StructureType wall = StructureType.None;
        StructureType floor = StructureType.None;
        int shelves = 0;
        bool forge = false;
        int stoveX = int.MinValue;
        int stoveY = int.MinValue;
        int doorX = int.MinValue;
        int doorY = int.MinValue;

        while (_queue.Count > 0)
        {
            (int cx, int cy) = _queue.Dequeue();
            if (_roomTiles.Count - tileStart >= MaxRoomTiles)
            {
                _roomTiles.RemoveRange(tileStart, _roomTiles.Count - tileStart);
                return;
            }

            _roomTiles.Add((cx, cy));
            minX = Math.Min(minX, cx);
            maxX = Math.Max(maxX, cx);
            minY = Math.Min(minY, cy);
            maxY = Math.Max(maxY, cy);
            if (grid.TryGetStructure(cx, cy, out StructureTile inner) && !inner.IsDestroyed)
            {
                if (inner.Type == StructureType.WoodShelf)
                {
                    shelves++;
                }
                else if (inner.Type == StructureType.ForgeAnvil)
                {
                    forge = true;
                }
                else if (inner.Type == StructureType.Stove)
                {
                    (stoveX, stoveY) = (cx, cy);
                }
                else if (inner.Category == StructureCategory.Floor && floor == StructureType.None)
                {
                    floor = inner.Type;
                }
            }

            for (int i = 0; i < s_cardinal.Length; i++)
            {
                int nx = cx + s_cardinal[i].X;
                int ny = cy + s_cardinal[i].Y;
                if (grid.IsRoomBoundaryAt(nx, ny))
                {
                    if (grid.TryGetStructure(nx, ny, out StructureTile w))
                    {
                        if (wall == StructureType.None && w.Category == StructureCategory.Wall)
                        {
                            wall = w.Type;
                        }
                        else if (doorX == int.MinValue && w.Category == StructureCategory.Door)
                        {
                            (doorX, doorY) = (nx, ny);
                        }
                    }

                    continue;
                }

                if (_visited.Add((nx, ny)))
                {
                    _queue.Enqueue((nx, ny));
                }
            }
        }

        int count = _roomTiles.Count - tileStart;
        int index = _rooms.Count;
        for (int i = tileStart; i < _roomTiles.Count; i++)
        {
            _tileToRoom[_roomTiles[i]] = index;
        }

        // The roof spans the interior plus the walls around it.
        RoofStyle style = ResolveStyle(wall, floor);
        RoofEmblem emblem = forge ? RoofEmblem.Anvil : shelves >= 3 ? RoofEmblem.Goods : RoofEmblem.None;
        bool rectangular = count == (maxX - minX + 1) * (maxY - minY + 1);
        _rooms.Add(new RoofRoom(minX - 1, minY - 1, maxX + 1, maxY + 1, tileStart, count, style, emblem, rectangular, stoveX, stoveY, doorX, doorY));
    }

    private static RoofStyle ResolveStyle(StructureType wall, StructureType floor) =>
        wall == StructureType.SandstoneWall ? RoofStyle.FlatClay :
        wall == StructureType.StoneWall ? RoofStyle.SnowSlate :
        wall == StructureType.MetalWall ? RoofStyle.Metal :
        floor == StructureType.PlankDeck ? RoofStyle.Thatch :
        RoofStyle.Shingle;

    private void DrawRectRoof(GeometryBatch batch, in RoofRoom room, float x0, float y0, float x1, float y1, float time, float alpha)
    {
        RoofPalette p = RoofPalette.For(room.Style).Faded(alpha);
        const float Lift = 7f;   // the roof floats above the wall tops (2.5D)
        const float Eave = 3f;   // overhang past the walls

        // The roof stops on the top of the south wall row, so the front facade (and a south door) stays in view.
        float left = x0 - Eave;
        float right = x1 + Eave;
        float top = y0 - Lift - Eave;
        float bottom = y1 - (_tileSize * 0.55f);
        float w = right - left;
        float h = bottom - top;

        // Shadow cast on the ground south of the house, then the front fascia that sells the height.
        // Soft shadow along the eaves on the facade, then the fascia board under the roof edge.
        batch.FillRectangle(new RectangleF(x0, bottom, x1 - x0, 5f), Fade(new ColorRgba(0, 0, 0, 60), alpha));
        batch.FillRectangle(new RectangleF(left, bottom - 2f, w, 4f), p.Fascia);

        if (room.Style == RoofStyle.FlatClay)
        {
            batch.FillRectangle(new RectangleF(left, top, w, h), p.Dark);
            batch.FillRectangle(new RectangleF(left + 4f, top + 4f, w - 8f, h - 8f), p.Light);
            for (float vy = top + 10f; vy < bottom - 8f; vy += 14f)
            {
                batch.DrawLine(new Vector2(left + 6f, vy), new Vector2(right - 6f, vy), p.Line, 1f);
            }

            // Roof hatch and water spouts.
            batch.FillRectangle(new RectangleF(left + (w * 0.62f), top + (h * 0.35f), 9f, 7f), p.Dark);
            batch.FillRectangle(new RectangleF(left + 6f, bottom - 2f, 4f, 5f), p.Fascia);
            batch.FillRectangle(new RectangleF(right - 10f, bottom - 2f, 4f, 5f), p.Fascia);
        }
        else
        {
            // Gable roof: the ridge runs along the long side; the far slope is lit, the near one in shade.
            bool ridgeX = w >= h;
            if (ridgeX)
            {
                float mid = top + (h * 0.5f);
                batch.FillRectangle(new RectangleF(left, top, w, mid - top), p.Light);
                batch.FillRectangle(new RectangleF(left, mid, w, bottom - mid), p.Dark);
                for (float sy = top + 5f; sy < bottom - 2f; sy += 6f)
                {
                    batch.DrawLine(new Vector2(left + 1f, sy), new Vector2(right - 1f, sy), p.Line, 1f);
                }

                if (room.Style == RoofStyle.Thatch)
                {
                    for (float sx = left + 3f; sx < right; sx += 5f)
                    {
                        batch.DrawLine(new Vector2(sx, top + 1f), new Vector2(sx + 1.5f, bottom - 1f), p.Line, 1f);
                    }
                }

                batch.DrawLine(new Vector2(left, mid), new Vector2(right, mid), p.Ridge, 2.5f);
                if (room.Style == RoofStyle.SnowSlate)
                {
                    batch.FillRectangle(new RectangleF(left, top, w, (mid - top) * 0.8f), p.Snow);
                    for (float sx = left + 2f; sx < right - 4f; sx += 9f)
                    {
                        batch.FillRectangle(new RectangleF(sx, top + ((mid - top) * 0.8f) - 1f, 4f, 3f), p.Snow);
                    }
                }
            }
            else
            {
                float mid = left + (w * 0.5f);
                batch.FillRectangle(new RectangleF(left, top, mid - left, h), p.Light);
                batch.FillRectangle(new RectangleF(mid, top, right - mid, h), p.Dark);
                for (float sx = left + 5f; sx < right - 2f; sx += 6f)
                {
                    batch.DrawLine(new Vector2(sx, top + 1f), new Vector2(sx, bottom - 1f), p.Line, 1f);
                }

                batch.DrawLine(new Vector2(mid, top), new Vector2(mid, bottom), p.Ridge, 2.5f);
                if (room.Style == RoofStyle.SnowSlate)
                {
                    batch.FillRectangle(new RectangleF(left, top, w, h * 0.35f), p.Snow);
                }
            }
        }

        batch.DrawRectangle(new RectangleF(left, top, w, h), p.Outline, 1.5f);

        // Chimney over the stove, with a slow wisp of smoke.
        if (room.StoveX != int.MinValue)
        {
            float cx = ((room.StoveX + 0.5f) * _tileSize) - 5f;
            float cy = (room.StoveY * _tileSize) - Lift - 6f;
            batch.FillRectangle(new RectangleF(cx - 1f, cy - 1f, 12f, 14f), p.Outline);
            batch.FillRectangle(new RectangleF(cx, cy, 10f, 12f), Fade(new ColorRgba(120, 113, 108, 255), alpha));
            batch.FillRectangle(new RectangleF(cx, cy, 10f, 3f), Fade(new ColorRgba(87, 83, 78, 255), alpha));
            for (int k = 0; k < 3; k++)
            {
                float phase = ((time * 0.45f) + (k * 0.33f)) % 1f;
                float r = 2.5f + (phase * 4f);
                byte smoke = (byte)(110 * (1f - phase));
                batch.FillCircle(new Vector2(cx + 5f + (MathF.Sin((time * 1.3f) + k) * 3f), cy - 3f - (phase * 22f)), r, Fade(new ColorRgba(210, 210, 215, smoke), alpha), 10);
            }
        }

        // Entrances on the side or back walls are hidden under the roof: mark them with a small awning and door.
        if (room.DoorX != int.MinValue && room.DoorY != room.MaxY)
        {
            this.DrawSideEntrance(batch, in room, p, alpha);
        }

        if (room.Emblem != RoofEmblem.None)
        {
            float sx = left + (w * 0.5f);
            float sy = bottom - 2f;
            this.DrawSign(batch, room.Emblem, sx, sy, alpha);
        }
    }

    private void DrawSideEntrance(GeometryBatch batch, in RoofRoom room, in RoofPalette p, float alpha)
    {
        float tx = room.DoorX * _tileSize;
        float ty = room.DoorY * _tileSize;
        float s = _tileSize;
        ColorRgba door = Fade(new ColorRgba(120, 72, 36, 255), alpha);
        ColorRgba frame = Fade(new ColorRgba(64, 38, 20, 255), alpha);
        if (room.DoorX == room.MinX || room.DoorX == room.MaxX)
        {
            // West or east wall: the door leaf seen edge-on under a little lean-to roof.
            bool west = room.DoorX == room.MinX;
            float ox = west ? tx - 7f : tx + s - 5f;
            batch.FillRectangle(new RectangleF(ox - 1f, ty + 3f, 14f, s - 2f), frame);
            batch.FillRectangle(new RectangleF(ox + 1f, ty + 5f, 10f, s - 6f), door);
            batch.FillRectangle(new RectangleF(west ? ox - 4f : ox - 2f, ty - 3f, 18f, 7f), p.Dark);
            batch.DrawRectangle(new RectangleF(west ? ox - 4f : ox - 2f, ty - 3f, 18f, 7f), p.Outline, 1f);
        }
        else
        {
            // North wall: a gabled porch roof poking out over the door.
            _poly[0] = new Vector2(tx - 4f, ty + 2f);
            _poly[1] = new Vector2(tx + s + 4f, ty + 2f);
            _poly[2] = new Vector2(tx + (s * 0.5f), ty - 12f);
            batch.FillConvexPolygon(_poly.AsSpan(0, 3), p.Light);
            batch.FillRectangle(new RectangleF(tx + 6f, ty - 2f, s - 12f, 6f), door);
        }
    }

    private void DrawSign(GeometryBatch batch, RoofEmblem emblem, float cx, float cy, float alpha)
    {
        RectangleF board = new(cx - 11f, cy - 2f, 22f, 14f);
        batch.FillRectangle(new RectangleF(board.X - 1f, board.Y - 1f, board.Width + 2f, board.Height + 2f), Fade(new ColorRgba(41, 28, 18, 255), alpha));
        batch.FillRectangle(board, Fade(new ColorRgba(146, 98, 52, 255), alpha));
        if (emblem == RoofEmblem.Anvil)
        {
            _poly[0] = new Vector2(cx - 7f, cy + 2f);
            _poly[1] = new Vector2(cx + 8f, cy + 2f);
            _poly[2] = new Vector2(cx + 4f, cy + 5f);
            _poly[3] = new Vector2(cx - 3f, cy + 5f);
            batch.FillConvexPolygon(_poly.AsSpan(0, 4), Fade(new ColorRgba(55, 60, 68, 255), alpha));
            batch.FillRectangle(new RectangleF(cx - 2f, cy + 5f, 4f, 3f), Fade(new ColorRgba(55, 60, 68, 255), alpha));
            batch.FillRectangle(new RectangleF(cx - 5f, cy + 8f, 10f, 2f), Fade(new ColorRgba(55, 60, 68, 255), alpha));
        }
        else
        {
            batch.FillCircle(new Vector2(cx, cy + 5f), 4.5f, Fade(new ColorRgba(180, 120, 20, 255), alpha), 14);
            batch.FillCircle(new Vector2(cx, cy + 5f), 3.2f, Fade(new ColorRgba(251, 191, 36, 255), alpha), 14);
            batch.FillRectangle(new RectangleF(cx - 0.6f, cy + 2.8f, 1.2f, 4.4f), Fade(new ColorRgba(180, 120, 20, 255), alpha));
        }
    }

    private void DrawTileRoof(GeometryBatch batch, BuildingGrid grid, in RoofRoom room, float alpha)
    {
        RoofPalette p = RoofPalette.For(room.Style).Faded(alpha);
        for (int i = room.TileStart; i < room.TileStart + room.TileCount; i++)
        {
            (int tx, int ty) = _roomTiles[i];
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int wx = tx + dx;
                    int wy = ty + dy;
                    bool boundary = grid.IsRoomBoundaryAt(wx, wy);
                    if ((dx != 0 || dy != 0) && !boundary)
                    {
                        continue;
                    }

                    ColorRgba col = (wy & 1) == 0 ? p.Light : p.Dark;
                    batch.FillRectangle(new RectangleF(wx * _tileSize, (wy * _tileSize) - 6f, _tileSize, _tileSize), col);
                }
            }
        }
    }

    private static ColorRgba Fade(ColorRgba color, float alpha) => color with { A = (byte)(color.A * alpha) };

    private enum RoofStyle : byte
    {
        Shingle,
        Thatch,
        SnowSlate,
        FlatClay,
        Metal,
    }

    private enum RoofEmblem : byte
    {
        None,
        Anvil,
        Goods,
    }

    private readonly record struct RoofRoom(
        int MinX,
        int MinY,
        int MaxX,
        int MaxY,
        int TileStart,
        int TileCount,
        RoofStyle Style,
        RoofEmblem Emblem,
        bool IsRectangular,
        int StoveX,
        int StoveY,
        int DoorX,
        int DoorY);

    private readonly record struct RoofPalette(ColorRgba Light, ColorRgba Dark, ColorRgba Line, ColorRgba Ridge, ColorRgba Fascia, ColorRgba Outline, ColorRgba Snow)
    {
        public RoofPalette Faded(float alpha) => new(
            Fade(this.Light, alpha),
            Fade(this.Dark, alpha),
            Fade(this.Line, alpha),
            Fade(this.Ridge, alpha),
            Fade(this.Fascia, alpha),
            Fade(this.Outline, alpha),
            Fade(this.Snow, alpha));

        public static RoofPalette For(RoofStyle style) => style switch
        {
            RoofStyle.Thatch => new(new(176, 148, 82), new(138, 112, 58), new(112, 90, 46), new(96, 76, 38), new(92, 70, 40), new(58, 44, 26), default),
            RoofStyle.SnowSlate => new(new(100, 110, 124), new(71, 80, 94), new(56, 63, 75), new(226, 232, 240), new(64, 58, 54), new(36, 40, 48), new(236, 241, 247)),
            RoofStyle.FlatClay => new(new(214, 178, 122), new(176, 138, 86), new(190, 154, 104), new(150, 112, 66), new(150, 108, 62), new(96, 66, 36), default),
            RoofStyle.Metal => new(new(148, 156, 166), new(110, 118, 130), new(92, 100, 112), new(180, 188, 198), new(78, 84, 94), new(44, 48, 56), default),
            RoofStyle.Shingle or _ => new(new(170, 76, 56), new(132, 54, 40), new(108, 42, 32), new(92, 36, 28), new(98, 64, 40), new(58, 26, 20), default),
        };
    }
}

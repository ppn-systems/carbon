// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Settlements;

namespace Carbon.Maps.Landmarks;

/// <summary>
/// Lays out a village along its streets (ADR-027). A well marks the small square; a main street runs through it, lined up
/// with the roads that reach the village, with a bend in each arm, and one or two side lanes branch off it. Every building
/// is generated (<see cref="VillageBuildingGenerator"/>), so no two footprints in a village are alike, and is placed
/// beside a street with its door turned to the street and a short path to it. Buildings keep clear of each other, of the
/// streets and of the square, and stand on dry ground at the site's level. A pure function of the site, so the server and
/// every client plan the same village.
/// </summary>
internal sealed class VillagePlanner(VillageStyle style, bool withFields) : ILandmarkPlanner
{
    private const int MaxHouses = 10;
    private const int MinHouses = 6;
    private const float MainStreetWidth = 1.1f;
    private const float LaneWidth = 0.9f;
    private const float SpurWidth = 0.6f;
    private const float SquareRadius = 3.6f;
    private const int FootprintTries = 6;
    private const int MaxHouseFailures = 3;
    private const int LayoutVariants = 4;
    private const int MinHousesWanted = 3;
    private const int MaxSetback = 3;
    private const float MaxSpurLength = 9f;

    // Door direction (outwards) of a building turned by 0..3 clockwise quarter turns.
    private static readonly (int X, int Y)[] s_doorDirections = [(0, 1), (-1, 0), (0, -1), (1, 0)];

    public void Plan(LandmarkPlanContext c)
    {
        const float Px = LandmarkPlanContext.Px;
        float cx = c.CenterX;
        float cy = c.CenterY;
        c.Add(new LandmarkPiece(LandmarkPieceKind.Well, cx, cy + (10f * Px), 1f, 0));

        // A street layout that leaves too little dry land for homes (a lake or a river bend) is laid out again another way.
        SiteTerrain terrain = new(c);
        List<Candidate> candidates = new(1024);
        Draft? best = null;
        for (int variant = 0; variant < LayoutVariants; variant++)
        {
            terrain.ClearMarks();
            Draft draft = new(c, variant * 5000);
            this.PlanVariant(draft, terrain, candidates);
            if (best is null || draft.Rank > best.Rank)
            {
                best = draft;
            }

            if (draft.Houses >= MinHousesWanted)
            {
                break;
            }
        }

        foreach (LandmarkPath path in best!.Paths)
        {
            c.Add(path);
        }

        foreach (VillagePlot plot in best.Plots)
        {
            c.Add(plot);
        }

        foreach ((int minX, int minY, int maxX, int maxY, byte crop) in best.Gardens)
        {
            c.Add(new LandmarkField(minX, minY, maxX + 1, maxY + 1, crop));
        }

        float barrelX = cx + (42f * Px);
        float barrelY = cy + (34f * Px);
        if (!c.OverlapsPlot((int)MathF.Floor(barrelX) - 1, (int)MathF.Floor(barrelY), (int)MathF.Floor(barrelX), (int)MathF.Floor(barrelY), margin: 0))
        {
            c.Add(new LandmarkPiece(LandmarkPieceKind.Barrel, barrelX, barrelY, 1f, 0));
        }
    }

    private void PlanVariant(Draft c, SiteTerrain terrain, List<Candidate> candidates)
    {
        foreach (LandmarkPath street in LayStreets(c, terrain))
        {
            c.Paths.Add(street);
            terrain.MarkPath(street);
        }

        List<LandmarkPath> streets = [.. c.Paths];
        List<VillageBuildingTemplate> built = [];
        int houses = MinHouses + (int)(c.Hash(600) % (MaxHouses - MinHouses + 1));
        VillageBuildingRole[] order = [VillageBuildingRole.Smithy, VillageBuildingRole.Store, VillageBuildingRole.Stall, VillageBuildingRole.Storehouse];
        int failures = 0;
        for (int k = 0; k < order.Length + houses + MaxHouseFailures && failures < MaxHouseFailures && built.Count < order.Length + houses; k++)
        {
            VillageBuildingRole role = k < order.Length ? order[k] : VillageBuildingRole.House;
            if (!this.TryPlaceBuilding(c, terrain, streets, built, candidates, role, k))
            {
                failures += role == VillageBuildingRole.House ? 1 : 0; // After a few houses that do not fit, the site is full.
            }
            else if (role == VillageBuildingRole.House && (withFields || c.Hash(1200 + k) % 2 == 0))
            {
                TryAddGarden(c, terrain, c.Plots[^1], k);
            }
        }
    }

    // The main street (two or three arms through the square) and two or three side lanes.
    private static List<LandmarkPath> LayStreets(Draft c, SiteTerrain terrain)
    {
        // Villages are not on the road network (ADR-027 design rules): the main street takes its own heading through the
        // square, with a third arm on some sites.
        float heading = c.Hash01(700) * MathF.Tau;
        List<float> headings = [heading, heading + MathF.PI + ((c.Hash01(701) - 0.5f) * 0.7f)];
        if (c.Hash(702) % 2 == 0)
        {
            headings.Add(heading + (MathF.PI * 0.5f) + ((c.Hash01(703) - 0.5f) * 0.8f));
        }

        List<LandmarkPath> streets = [];
        List<(float Angle, float Length)> arms = [];
        float reach = c.Radius - 1.5f;
        for (int i = 0; i < Math.Min(headings.Count, 3); i++)
        {
            float bend = (c.Hash01(710 + i) - 0.5f) * 0.6f;
            float length = AddArm(c, terrain, streets, c.CenterX, c.CenterY, headings[i], bend, reach, MainStreetWidth);
            if (length > 0f)
            {
                arms.Add((headings[i], length));
            }
        }

        int lanes = arms.Count == 0 ? 0 : 3 + (int)(c.Hash(720) % 2);
        for (int i = 0; i < lanes; i++)
        {
            (float angle, float length) = arms[(int)(c.Hash(721 + i) % (uint)arms.Count)];
            float at = length * (0.45f + (c.Hash01(725 + i) * 0.4f)); // well clear of the square: no crossroads at the well
            float sx = c.CenterX + (LandmarkPlanContext.Cos(angle) * at);
            float sy = c.CenterY + (LandmarkPlanContext.Sin(angle) * at);
            float side = (c.Hash(730 + i) & 1) == 0 ? 1f : -1f;
            float laneAngle = angle + (side * ((MathF.PI / 2f) + ((c.Hash01(735 + i) - 0.5f) * 0.6f)));
            float laneLength = 7f + (c.Hash01(740 + i) * 6f);
            _ = AddArm(c, terrain, streets, sx, sy, laneAngle, (c.Hash01(745 + i) - 0.5f) * 0.5f, laneLength, LaneWidth);
        }

        return streets;
    }

    // Adds a street arm of two bent segments that stops where the ground stops being buildable. Returns its length.
    private static float AddArm(Draft c, SiteTerrain terrain, List<LandmarkPath> streets, float x, float y, float angle, float bend, float maxLength, float width)
    {
        float half = maxLength * 0.5f;
        float total = 0f;
        for (int segment = 0; segment < 2; segment++)
        {
            float a = angle + (segment * bend);
            float dx = LandmarkPlanContext.Cos(a);
            float dy = LandmarkPlanContext.Sin(a);
            float length = 0f;
            while (length < half)
            {
                float nx = x + (dx * (length + 0.5f));
                float ny = y + (dy * (length + 0.5f));
                if (!terrain.IsBuildable((int)MathF.Floor(nx), (int)MathF.Floor(ny)) || !WithinRadius(c, nx, ny, c.Radius - 1f))
                {
                    break;
                }

                length += 0.5f;
            }

            if (length < 1f)
            {
                break;
            }

            float ex = x + (dx * length);
            float ey = y + (dy * length);
            streets.Add(new LandmarkPath(x, y, ex, ey, width));
            total += length;
            x = ex;
            y = ey;
            if (length < half)
            {
                break;
            }
        }

        return total;
    }

    private bool TryPlaceBuilding(
        Draft c,
        SiteTerrain terrain,
        List<LandmarkPath> streets,
        List<VillageBuildingTemplate> built,
        List<Candidate> candidates,
        VillageBuildingRole role,
        int order)
    {
        int clearance = 2 + (int)(c.Hash(800 + order) % 2);
        float gap = c.Hash(820 + order) % 2 * 0.5f;
        bool compact = false;
        for (int attempt = 0; attempt < FootprintTries; attempt++)
        {
            ulong seed = c.Seed ^ ((ulong)c.Hash(900 + (order * 16) + attempt) << 32) ^ c.Hash(1900 + (order * 16) + attempt);
            VillageBuildingTemplate template = VillageBuildingGenerator.Generate(role, seed, compact);
            if (role != VillageBuildingRole.Stall && built.Exists(t => t.HasSameFootprint(template)))
            {
                continue; // Every building of a village has its own footprint: roll another.
            }

            // Frontage on a street first; when none is left, stand back anywhere with a longer path to the nearest street.
            candidates.Clear();
            this.StreetCandidates(c, streets, template, role, order, gap, candidates);
            if (this.TryCommitBest(c, terrain, candidates, clearance))
            {
                built.Add(template);
                return true;
            }

            candidates.Clear();
            this.BackCandidates(c, terrain, streets, template, role, order, candidates);
            if (this.TryCommitBest(c, terrain, candidates, clearance))
            {
                built.Add(template);
                return true;
            }

            // No room for this footprint anywhere: try a compact one, and give up when even that does not fit.
            if (compact)
            {
                return false;
            }

            compact = true;
        }

        return false;
    }

    private void StreetCandidates(
        Draft c,
        List<LandmarkPath> streets,
        VillageBuildingTemplate template,
        VillageBuildingRole role,
        int order,
        float gap,
        List<Candidate> output)
    {
        foreach (LandmarkPath street in streets)
        {
            float dx = street.X1 - street.X0;
            float dy = street.Y1 - street.Y0;
            float length = MathF.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.5f)
            {
                continue;
            }

            dx /= length;
            dy /= length;
            for (float t = 0f; t <= length; t += 1f)
            {
                float px = street.X0 + (dx * t);
                float py = street.Y0 + (dy * t);
                for (int side = -1; side <= 1; side += 2)
                {
                    float nx = -dy * side;
                    float ny = dx * side;
                    int turns = FacingTurns(-nx, -ny);
                    (int w, int h) = template.RotatedSize(turns);
                    VillagePlot probe = new(template, role, style, 0, 0, (byte)turns, (byte)w, (byte)h);
                    (int f0x, int f0y) = VillageBlueprint.FrontTile(probe);

                    // Set back from the street by a varied distance, so the frontage is not one straight row.
                    for (int extra = 0; extra < MaxSetback; extra++)
                    {
                        float setback = (street.Width * 0.5f) + 1f + gap + extra;
                        int fx = (int)MathF.Floor(px + (nx * setback));
                        int fy = (int)MathF.Floor(py + (ny * setback));
                        VillagePlot plot = probe with { OriginX = fx - f0x, OriginY = fy - f0y };
                        float score = Score(c, role, fx, fy, output.Count, order) + (extra * 0.6f);
                        output.Add(new Candidate(plot, fx, fy, px, py, score, output.Count));
                    }
                }
            }
        }
    }

    private void BackCandidates(
        Draft c,
        SiteTerrain terrain,
        List<LandmarkPath> streets,
        VillageBuildingTemplate template,
        VillageBuildingRole role,
        int order,
        List<Candidate> output)
    {
        float limit = c.Radius - 0.5f;
        for (int oy = terrain.MinY; oy <= terrain.MaxY; oy++)
        {
            for (int ox = terrain.MinX; ox <= terrain.MaxX; ox++)
            {
                for (int turns = 0; turns < 4; turns++)
                {
                    (int w, int h) = template.RotatedSize(turns);
                    if (!WithinRadius(c, ox + 0.5f, oy + 0.5f, limit) || !WithinRadius(c, ox + w - 0.5f, oy + h - 0.5f, limit))
                    {
                        continue;
                    }

                    VillagePlot plot = new(template, role, style, ox, oy, (byte)turns, (byte)w, (byte)h);
                    (int fx, int fy) = VillageBlueprint.FrontTile(plot);
                    if (!NearestStreetPoint(streets, fx + 0.5f, fy + 0.5f, out float px, out float py, out float distance) || distance > MaxSpurLength)
                    {
                        continue;
                    }

                    (int doorX, int doorY) = s_doorDirections[turns];
                    float facing = ((doorX * (px - fx - 0.5f)) + (doorY * (py - fy - 0.5f))) / MathF.Max(distance, 0.01f);
                    if (facing < 0.6f)
                    {
                        continue;
                    }

                    float score = Score(c, role, fx, fy, output.Count, order) + 3f + distance;
                    output.Add(new Candidate(plot, fx, fy, px, py, score, output.Count));
                }
            }
        }
    }

    private bool TryCommitBest(Draft c, SiteTerrain terrain, List<Candidate> candidates, int clearance)
    {
        // Ties keep generation order, so every platform commits the same candidate.
        candidates.Sort(static (a, b) => a.Score != b.Score ? a.Score.CompareTo(b.Score) : a.Index.CompareTo(b.Index));
        foreach (Candidate k in candidates)
        {
            if (!this.IsValid(c, terrain, k.Plot, clearance, k.FrontX, k.FrontY, k.StreetX, k.StreetY))
            {
                continue;
            }

            c.Plots.Add(k.Plot);
            terrain.MarkPlot(k.Plot);
            LandmarkPath spur = new(k.FrontX + 0.5f, k.FrontY + 0.5f, k.StreetX, k.StreetY, SpurWidth);
            c.Paths.Add(spur);
            terrain.MarkPath(spur);
            return true;
        }

        return false;
    }

    // Lower is better. Shops sit close to the square; homes take any frontage, close to the street.
    private static float Score(Draft c, VillageBuildingRole role, int fx, int fy, int candidate, int order)
    {
        float ex = fx + 0.5f - c.CenterX;
        float ey = fy + 0.5f - c.CenterY;
        float distance = MathF.Sqrt((ex * ex) + (ey * ey));
        float noise = (c.Hash(10_000 + (order * 8192) + candidate) & 0xFFFF) / 65535f;
        return role switch
        {
            VillageBuildingRole.Stall => distance,
            VillageBuildingRole.Smithy or VillageBuildingRole.Store => distance + (noise * 4f),
            VillageBuildingRole.House => noise * 2f,
            VillageBuildingRole.Storehouse => (distance * 0.3f) + (noise * 4f),
            _ => noise,
        };
    }

    // A kitchen garden beside a home (one tile from its side or back wall), on open dry ground off streets and other plots.
    private static void TryAddGarden(Draft c, SiteTerrain terrain, in VillagePlot home, int order)
    {
        int gw = 3 + (int)(c.Hash(1300 + order) % 3);
        int gh = 3 + (int)(c.Hash(1310 + order) % 2);
        int start = (int)(c.Hash(1320 + order) % 4);
        (int ex, int ey) = VillageBlueprint.EntranceTile(home);
        int midX = home.OriginX + (home.Width / 2);
        int midY = home.OriginY + (home.Height / 2);
        for (int k = 0; k < 4; k++)
        {
            (int x0, int y0) = ((start + k) % 4) switch
            {
                0 => (home.OriginX - 1 - gw, midY - (gh / 2)),
                1 => (home.OriginX + home.Width + 1, midY - (gh / 2)),
                2 => (midX - (gw / 2), home.OriginY - 1 - gh),
                _ => (midX - (gw / 2), home.OriginY + home.Height + 1),
            };
            int x1 = x0 + gw - 1;
            int y1 = y0 + gh - 1;

            // Never in front of the door.
            bool front = ex >= x0 - 1 && ex <= x1 + 1 && ey >= y0 - 2 && ey <= y1 + 2;
            if (front || !WithinRadius(c, x0 + 0.5f, y0 + 0.5f, c.Radius - 0.5f) || !WithinRadius(c, x1 + 0.5f, y1 + 0.5f, c.Radius - 0.5f) ||
                !WithinRadius(c, x0 + 0.5f, y1 + 0.5f, c.Radius - 0.5f) || !WithinRadius(c, x1 + 0.5f, y0 + 0.5f, c.Radius - 0.5f) ||
                c.OverlapsPlot(x0, y0, x1, y1, margin: 1) || c.OverlapsGarden(x0, y0, x1, y1, margin: 1))
            {
                continue;
            }

            bool clear = true;
            for (int y = y0; y <= y1 && clear; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!terrain.IsBuildable(x, y) || terrain.IsPath(x, y) || terrain.IsPlot(x, y) || terrain.IsRoad(x, y))
                    {
                        clear = false;
                        break;
                    }
                }
            }

            if (clear)
            {
                c.Gardens.Add((x0, y0, x1, y1, (byte)(c.Hash(1330 + order) % 3)));
                terrain.MarkRect(x0, y0, x1, y1);
                return;
            }
        }
    }

    private static bool NearestStreetPoint(List<LandmarkPath> streets, float x, float y, out float px, out float py, out float distance)
    {
        px = 0f;
        py = 0f;
        float best = float.MaxValue;
        foreach (LandmarkPath s in streets)
        {
            float dx = s.X1 - s.X0;
            float dy = s.Y1 - s.Y0;
            float len2 = (dx * dx) + (dy * dy);
            float t = len2 <= 1e-6f ? 0f : Math.Clamp((((x - s.X0) * dx) + ((y - s.Y0) * dy)) / len2, 0f, 1f);
            float qx = s.X0 + (dx * t);
            float qy = s.Y0 + (dy * t);
            float d2 = ((qx - x) * (qx - x)) + ((qy - y) * (qy - y));
            if (d2 < best)
            {
                best = d2;
                px = qx;
                py = qy;
            }
        }

        distance = MathF.Sqrt(best);
        return streets.Count > 0;
    }

    private static int FacingTurns(float dirX, float dirY)
    {
        int best = 0;
        float bestDot = float.MinValue;
        for (int t = 0; t < 4; t++)
        {
            float dot = (s_doorDirections[t].X * dirX) + (s_doorDirections[t].Y * dirY);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = t;
            }
        }

        return best;
    }

    // Cheap checks first (radius, square, other buildings, paths), terrain last: every terrain sample is expensive.
    private bool IsValid(Draft c, SiteTerrain terrain, in VillagePlot plot, int clearance, int fx, int fy, float streetX, float streetY)
    {
        int minX = plot.OriginX;
        int minY = plot.OriginY;
        int maxX = minX + plot.Width - 1;
        int maxY = minY + plot.Height - 1;
        float limit = c.Radius - 0.5f;
        if (!WithinRadius(c, minX + 0.5f, minY + 0.5f, limit) || !WithinRadius(c, maxX + 0.5f, minY + 0.5f, limit) ||
            !WithinRadius(c, minX + 0.5f, maxY + 0.5f, limit) || !WithinRadius(c, maxX + 0.5f, maxY + 0.5f, limit))
        {
            return false;
        }

        float qx = Math.Clamp(c.CenterX, minX, maxX + 1) - c.CenterX;
        float qy = Math.Clamp(c.CenterY, minY, maxY + 1) - c.CenterY;
        if ((qx * qx) + (qy * qy) < SquareRadius * SquareRadius || c.OverlapsPlot(minX, minY, maxX, maxY, clearance) ||
            c.OverlapsGarden(minX, minY, maxX, maxY, margin: 1))
        {
            return false;
        }

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (terrain.IsPath(x, y))
                {
                    return false;
                }
            }
        }

        // The path from the door to the street crosses open ground only (no wall of this or another building).
        float sx = fx + 0.5f;
        float sy = fy + 0.5f;
        float ddx = streetX - sx;
        float ddy = streetY - sy;
        int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt((ddx * ddx) + (ddy * ddy)) * 4f));
        for (int k = 0; k <= steps; k++)
        {
            float t = k / (float)steps;
            int tx = (int)MathF.Floor(sx + (ddx * t));
            int ty = (int)MathF.Floor(sy + (ddy * t));
            char cell = plot.CellAtWorld(tx, ty);
            if (terrain.IsPlot(tx, ty) || cell is not (VillageBuildingTemplate.EmptyCell or VillageBuildingTemplate.PorchCell or VillageBuildingTemplate.YardCell))
            {
                return false;
            }
        }

        // Dry ground at the site's level under the building and a tile around it, no road through it, and the path too.
        for (int y = minY - 1; y <= maxY + 1; y++)
        {
            for (int x = minX - 1; x <= maxX + 1; x++)
            {
                if (!terrain.IsBuildable(x, y))
                {
                    return false;
                }
            }
        }

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (terrain.IsRoad(x, y))
                {
                    return false;
                }
            }
        }

        for (int k = 0; k <= steps; k++)
        {
            float t = k / (float)steps;
            if (!terrain.IsBuildable((int)MathF.Floor(sx + (ddx * t)), (int)MathF.Floor(sy + (ddy * t))))
            {
                return false;
            }
        }

        return true;
    }

    private static bool WithinRadius(Draft c, float x, float y, float limit)
    {
        float ex = x - c.CenterX;
        float ey = y - c.CenterY;
        return (ex * ex) + (ey * ey) <= limit * limit;
    }

    /// <summary>One attempt at the village's streets and buildings, committed to the context only if it is kept.</summary>
    private sealed class Draft(LandmarkPlanContext context, int salt)
    {
        public List<LandmarkPath> Paths { get; } = [];

        public List<VillagePlot> Plots { get; } = [];

        public List<(int MinX, int MinY, int MaxX, int MaxY, byte Crop)> Gardens { get; } = [];

        public bool OverlapsGarden(int minX, int minY, int maxX, int maxY, int margin)
        {
            foreach ((int gx0, int gy0, int gx1, int gy1, _) in this.Gardens)
            {
                if (minX - margin <= gx1 && maxX + margin >= gx0 && minY - margin <= gy1 && maxY + margin >= gy0)
                {
                    return true;
                }
            }

            return false;
        }

        public PointOfInterest Poi => context.Poi;

        public float CenterX => context.CenterX;

        public float CenterY => context.CenterY;

        public float Radius => context.Radius;

        public ulong Seed => context.Seed ^ (ulong)salt;

        public int Houses => this.Plots.FindAll(static p => p.Role == VillageBuildingRole.House).Count;

        // Enough homes first, then as many buildings as possible.
        public int Rank => (this.Houses >= MinHousesWanted ? 1000 : 0) + this.Plots.Count;

        public uint Hash(int i) => context.Hash(i + salt);

        public float Hash01(int i) => context.Hash01(i + salt);

        public bool OverlapsPlot(int minX, int minY, int maxX, int maxY, int margin)
        {
            foreach (VillagePlot p in this.Plots)
            {
                if (minX - margin <= p.OriginX + p.Width - 1 && maxX + margin >= p.OriginX &&
                    minY - margin <= p.OriginY + p.Height - 1 && maxY + margin >= p.OriginY)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private readonly record struct Candidate(VillagePlot Plot, int FrontX, int FrontY, float StreetX, float StreetY, float Score, int Index);

    /// <summary>
    /// Terrain, roads, paths and plots of the site. Roads are rasterized once for the whole site (a per-tile road query
    /// costs about a hundred microseconds); terrain is sampled lazily and remembered.
    /// </summary>
    private sealed class SiteTerrain
    {
        private const byte BuildableKnown = 1;
        private const byte Buildable = 2;
        private const byte Road = 8;
        private const byte PathFlag = 16;
        private const byte PlotFlag = 32;

        private readonly LandmarkPlanContext _context;
        private readonly int _x0;
        private readonly int _y0;
        private readonly int _size;
        private readonly byte[] _cells;

        public SiteTerrain(LandmarkPlanContext context)
        {
            _context = context;
            int r = (int)MathF.Ceiling(context.Radius) + 2;
            _x0 = context.Poi.TileX - r;
            _y0 = context.Poi.TileY - r;
            _size = (2 * r) + 1;
            _cells = new byte[_size * _size];
            bool[] roads = new bool[_cells.Length];
            context.RasterizeRoads(_x0, _y0, _size, _size, roads);
            for (int i = 0; i < roads.Length; i++)
            {
                _cells[i] = roads[i] ? Road : (byte)0;
            }
        }

        public int MinX => _x0 + 1;

        public int MinY => _y0 + 1;

        public int MaxX => _x0 + _size - 2;

        public int MaxY => _y0 + _size - 2;

        public bool IsBuildable(int x, int y)
        {
            int i = this.IndexOf(x, y);
            if (i < 0)
            {
                return false;
            }

            if ((_cells[i] & BuildableKnown) == 0)
            {
                _cells[i] |= (byte)(BuildableKnown | (_context.IsBuildableTile(x, y) ? Buildable : 0));
            }

            return (_cells[i] & Buildable) != 0;
        }

        public bool IsRoad(int x, int y)
        {
            int i = this.IndexOf(x, y);
            return i < 0 ? _context.IsRoad(x, y) : (_cells[i] & Road) != 0;
        }

        public void ClearMarks()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] &= unchecked((byte)~(PathFlag | PlotFlag));
            }
        }

        public bool IsPath(int x, int y)
        {
            int i = this.IndexOf(x, y);
            return i >= 0 && (_cells[i] & PathFlag) != 0;
        }

        public bool IsPlot(int x, int y)
        {
            int i = this.IndexOf(x, y);
            return i >= 0 && (_cells[i] & PlotFlag) != 0;
        }

        // A tile is on a path when its centre lies within the path's half width plus half a tile.
        public void MarkPath(in LandmarkPath path)
        {
            float reach = (path.Width * 0.5f) + 0.45f;
            int minX = (int)MathF.Floor(MathF.Min(path.X0, path.X1) - reach);
            int maxX = (int)MathF.Floor(MathF.Max(path.X0, path.X1) + reach);
            int minY = (int)MathF.Floor(MathF.Min(path.Y0, path.Y1) - reach);
            int maxY = (int)MathF.Floor(MathF.Max(path.Y0, path.Y1) + reach);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int i = this.IndexOf(x, y);
                    if (i >= 0 && path.DistanceSquaredTo(x + 0.5f, y + 0.5f) <= reach * reach)
                    {
                        _cells[i] |= PathFlag;
                    }
                }
            }
        }

        // Marks a garden as taken, so no later building or door path crosses it.
        public void MarkRect(int minX, int minY, int maxX, int maxY)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int i = this.IndexOf(x, y);
                    if (i >= 0)
                    {
                        _cells[i] |= PlotFlag;
                    }
                }
            }
        }

        public void MarkPlot(in VillagePlot plot)
        {
            for (int y = plot.OriginY; y < plot.OriginY + plot.Height; y++)
            {
                for (int x = plot.OriginX; x < plot.OriginX + plot.Width; x++)
                {
                    int i = this.IndexOf(x, y);
                    if (i >= 0)
                    {
                        _cells[i] |= PlotFlag;
                    }
                }
            }
        }

        private int IndexOf(int x, int y)
        {
            int lx = x - _x0;
            int ly = y - _y0;
            return (uint)lx < (uint)_size && (uint)ly < (uint)_size ? (ly * _size) + lx : -1;
        }
    }
}

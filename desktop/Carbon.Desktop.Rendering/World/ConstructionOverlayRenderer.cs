// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Camera;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;

namespace Carbon.Desktop.Rendering.World;

/// <summary>
/// Draws the client-side building UX on top of the world: the placement ghost (structure <c>.cvg.json</c> art, green
/// when the server rules accept it, red with the reason otherwise), blueprint and frame sites with construction
/// progress, and deconstruction progress. Render-only: every mutation is decided by the server.
/// </summary>
internal sealed class ConstructionOverlayRenderer
{
    private const float LabelRadiusPx = 260f;

    private static readonly ColorRgba s_validFill = new(34, 197, 94, 70);
    private static readonly ColorRgba s_validBorder = new(74, 222, 128, 235);
    private static readonly ColorRgba s_invalidFill = new(239, 68, 68, 90);
    private static readonly ColorRgba s_invalidBorder = new(248, 113, 113, 235);
    private static readonly ColorRgba s_blueprintFill = new(59, 130, 246, 40);
    private static readonly ColorRgba s_blueprintBorder = new(96, 165, 250, 220);
    private static readonly ColorRgba s_frameBorder = new(217, 160, 82, 230);
    private static readonly ColorRgba s_scaffold = new(180, 130, 70, 170);
    private static readonly ColorRgba s_barBack = new(15, 23, 42, 210);
    private static readonly ColorRgba s_buildBar = new(34, 197, 94, 255);
    private static readonly ColorRgba s_deconstructBar = new(249, 115, 22, 255);
    private static readonly ColorRgba s_textOutline = new(10, 12, 18, 255);

    private readonly List<ConstructionMarker> _markers = new(32);

    private bool _hasGhost;
    private int _ghostX;
    private int _ghostY;
    private int _ghostWidth;
    private int _ghostHeight;
    private StructureType _ghostType;
    private BuildResultCode _ghostCode;

    /// <summary>Gets the result code of the last drawn placement ghost (for tests and HUD hints).</summary>
    public BuildResultCode LastGhostCode => _ghostCode;

    /// <summary>Snapshots the markers known to the client for this frame.</summary>
    public void BeginFrame(IWorldReadModel world)
    {
        world.CopyConstructionMarkers(_markers);
        _hasGhost = false;
    }

    /// <summary>
    /// Draws the placement ghost when the selected hotbar item is a placeable blueprint kit.
    /// </summary>
    /// <returns><c>true</c> when a ghost was drawn (the caller then skips the plain tile highlight).</returns>
    public bool TryDrawPlacementGhost(GeometryBatch batch, IWorldReadModel world, IVectorArtRepository? art, float tileSize, int tileX, int tileY)
    {
        if (!TryGetHeldBlueprint(world, out StructureType type))
        {
            return false;
        }

        BuildResultCode code = world.PreviewPlacement(tileX, tileY, type, StructureRotation.North, out int width, out int height);
        bool valid = code == BuildResultCode.Ok;
        DrawFootprintArt(batch, art, type, tileX, tileY, width, height, tileSize, valid ? (byte)130 : (byte)90);

        RectangleF area = new(tileX * tileSize, tileY * tileSize, width * tileSize, height * tileSize);
        batch.FillRectangle(Inset(area, 2f), valid ? s_validFill : s_invalidFill);
        batch.DrawRectangle(Inset(area, 1f), valid ? s_validBorder : s_invalidBorder, 1.6f);
        if (!valid)
        {
            batch.DrawLine(new Vector2(area.X + 5f, area.Y + 5f), new Vector2(area.X + area.Width - 5f, area.Y + area.Height - 5f), s_invalidBorder, 1.6f);
            batch.DrawLine(new Vector2(area.X + area.Width - 5f, area.Y + 5f), new Vector2(area.X + 5f, area.Y + area.Height - 5f), s_invalidBorder, 1.6f);
        }

        _hasGhost = true;
        _ghostX = tileX;
        _ghostY = tileY;
        _ghostWidth = width;
        _ghostHeight = height;
        _ghostType = type;
        _ghostCode = code;
        return true;
    }

    /// <summary>Draws blueprint / frame sites and deconstruction bars inside the visible world rectangle.</summary>
    public void DrawMarkers(GeometryBatch batch, IVectorArtRepository? art, float tileSize, float minX, float minY, float maxX, float maxY, float animTime)
    {
        for (int i = 0; i < _markers.Count; i++)
        {
            ConstructionMarker m = _markers[i];
            RectangleF area = new(m.AnchorX * tileSize, m.AnchorY * tileSize, m.Width * tileSize, m.Height * tileSize);
            if (area.X > maxX || area.Y > maxY || area.X + area.Width < minX || area.Y + area.Height < minY)
            {
                continue;
            }

            if (m.IsDeconstruction)
            {
                byte pulse = (byte)(150 + (int)(80f * (0.5f + (0.5f * MathF.Sin(animTime * 6f)))));
                batch.DrawRectangle(Inset(area, 1f), new ColorRgba(249, 115, 22, pulse), 1.6f);
                DrawProgressBar(batch, area, m.Progress, s_deconstructBar);
                continue;
            }

            if (m.Stage == ConstructionStage.Frame)
            {
                DrawFootprintArt(batch, art, m.Type, m.AnchorX, m.AnchorY, m.Width, m.Height, tileSize, (byte)(70 + (int)(150f * m.Progress)));
                DrawScaffold(batch, area);
                batch.DrawRectangle(Inset(area, 1f), s_frameBorder, 1.4f);
            }
            else
            {
                DrawFootprintArt(batch, art, m.Type, m.AnchorX, m.AnchorY, m.Width, m.Height, tileSize, 70);
                batch.FillRectangle(Inset(area, 2f), s_blueprintFill);
                DrawDashedRectangle(batch, Inset(area, 1f), s_blueprintBorder, 1.4f, dash: 5f, gap: 3f);
            }

            DrawProgressBar(batch, area, m.Progress, s_buildBar);
        }
    }

    /// <summary>Draws the ghost reason label and site / deconstruction labels near the player (screen space).</summary>
    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, CameraRig2D cameraRig, Vector2 playerPos, float tileSize)
    {
        if (_hasGhost)
        {
            string label = _ghostCode == BuildResultCode.Ok
                ? GameText.Format("build.place_blueprint", new TextArg("name", GameText.StructureName(_ghostType)))
                : BuildResultText.Describe(_ghostCode);
            ColorRgba color = _ghostCode == BuildResultCode.Ok ? new ColorRgba(134, 239, 172, 255) : new ColorRgba(252, 165, 165, 255);
            Vector2 top = new((_ghostX + (_ghostWidth * 0.5f)) * tileSize, _ghostY * tileSize);
            DrawCenteredLabel(spriteBatch, font, cameraRig, top, label, color, offsetY: 16f);
        }

        float radiusSq = LabelRadiusPx * LabelRadiusPx;
        for (int i = 0; i < _markers.Count; i++)
        {
            ConstructionMarker m = _markers[i];
            Vector2 top = new((m.AnchorX + (m.Width * 0.5f)) * tileSize, m.AnchorY * tileSize);
            if (Vector2.DistanceSquared(top, playerPos) > radiusSq)
            {
                continue;
            }

            int percent = (int)MathF.Round(m.Progress * 100f);
            string label = m.IsDeconstruction
                ? GameText.Format("build.deconstructing", new TextArg("percent", percent))
                : m.Stage == ConstructionStage.Frame
                    ? GameText.Format("build.progress", new TextArg("percent", percent))
                    : GameText.Get("build.needs_materials");
            ColorRgba color = m.IsDeconstruction ? new ColorRgba(253, 186, 116, 255) : new ColorRgba(191, 219, 254, 255);
            DrawCenteredLabel(spriteBatch, font, cameraRig, top, label, color, offsetY: 22f);
        }
    }

    private static bool TryGetHeldBlueprint(IWorldReadModel world, out StructureType type)
    {
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        ItemSlot held = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;
        if (!held.IsEmpty &&
            world.Items.Rules.Items.TryGet(held.ItemId, out ItemArchetype? archetype) &&
            archetype.Category == ItemCategory.Placeable &&
            archetype.PlaceableStructureType != 0)
        {
            type = (StructureType)archetype.PlaceableStructureType;
            return true;
        }

        type = StructureType.None;
        return false;
    }

    private static void DrawFootprintArt(
        GeometryBatch batch,
        IVectorArtRepository? art,
        StructureType type,
        int anchorX,
        int anchorY,
        int width,
        int height,
        float tileSize,
        byte alpha)
    {
        if (art is null || !art.TryGetStructure((byte)type, out CompiledStructureArt? structArt) || structArt.Shapes.Length == 0)
        {
            return;
        }

        BonePose pose = BonePose.Identity;
        float scale = tileSize / 32f;
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
            {
                Vector2 origin = new((anchorX + dx) * tileSize, (anchorY + dy) * tileSize);
                CompiledVectorShape[] shapes = structArt.Shapes;
                for (int i = 0; i < shapes.Length; i++)
                {
                    VectorRigRenderer.DrawShapeAt(batch, in shapes[i], in pose, origin, scale, scale, hitFlash: 0f, alpha: alpha);
                }
            }
        }
    }

    private static void DrawScaffold(GeometryBatch batch, RectangleF area)
    {
        const float step = 8f;
        float left = area.X + 2f;
        float top = area.Y + 2f;
        float right = area.X + area.Width - 2f;
        float bottom = area.Y + area.Height - 2f;
        for (float offset = step; offset < right - left + (bottom - top); offset += step)
        {
            // Diagonal from the left / top edge to the bottom / right edge, clipped to the footprint.
            float x0 = left + MathF.Max(0f, offset - (bottom - top));
            float y0 = MathF.Min(bottom, top + offset);
            float x1 = MathF.Min(right, left + offset);
            float y1 = top + MathF.Max(0f, offset - (right - left));
            batch.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), s_scaffold, 1f);
        }
    }

    private static void DrawProgressBar(GeometryBatch batch, RectangleF area, float progress, ColorRgba fill)
    {
        float width = MathF.Max(8f, area.Width - 6f);
        RectangleF back = new(area.X + 3f, area.Y - 7f, width, 4f);
        batch.FillRectangle(back, s_barBack);
        float filled = width * Math.Clamp(progress, 0f, 1f);
        if (filled > 0.5f)
        {
            batch.FillRectangle(new RectangleF(back.X, back.Y, filled, back.Height), fill);
        }
    }

    private static void DrawDashedRectangle(GeometryBatch batch, RectangleF rect, ColorRgba color, float thickness, float dash, float gap)
    {
        DrawDashedLine(batch, new Vector2(rect.X, rect.Y), new Vector2(rect.X + rect.Width, rect.Y), color, thickness, dash, gap);
        DrawDashedLine(batch, new Vector2(rect.X + rect.Width, rect.Y), new Vector2(rect.X + rect.Width, rect.Y + rect.Height), color, thickness, dash, gap);
        DrawDashedLine(batch, new Vector2(rect.X + rect.Width, rect.Y + rect.Height), new Vector2(rect.X, rect.Y + rect.Height), color, thickness, dash, gap);
        DrawDashedLine(batch, new Vector2(rect.X, rect.Y + rect.Height), new Vector2(rect.X, rect.Y), color, thickness, dash, gap);
    }

    private static void DrawDashedLine(GeometryBatch batch, Vector2 a, Vector2 b, ColorRgba color, float thickness, float dash, float gap)
    {
        float length = Vector2.Distance(a, b);
        if (length < 0.01f)
        {
            return;
        }

        Vector2 dir = (b - a) / length;
        for (float t = 0f; t < length; t += dash + gap)
        {
            float end = MathF.Min(length, t + dash);
            batch.DrawLine(a + (dir * t), a + (dir * end), color, thickness);
        }
    }

    private static void DrawCenteredLabel(SpriteBatch spriteBatch, BitmapFont font, CameraRig2D cameraRig, Vector2 worldTop, string label, ColorRgba color, float offsetY)
    {
        Vector2 screen = cameraRig.Camera.WorldToScreen(in worldTop);
        TextMeasureResult size = font.MeasureString(label, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(
            font,
            label,
            new Vector2(MathF.Floor(screen.X - (size.Width * 0.5f)), MathF.Floor(screen.Y - offsetY - size.Height)),
            color,
            s_textOutline,
            1,
            1f);
    }

    private static RectangleF Inset(RectangleF rect, float by) =>
        new(rect.X + by, rect.Y + by, MathF.Max(0f, rect.Width - (2f * by)), MathF.Max(0f, rect.Height - (2f * by)));
}

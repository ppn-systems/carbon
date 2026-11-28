// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Items.Races;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Desktop.Rendering.Camera;

namespace Carbon.Desktop.Rendering.Overlays;

/// <summary>
/// Screen-space overhead overlay renderer for monster/NPC/player health plates (with smooth ghost-damage trails
/// and rank badges), compact entity namepills, prop durability meters, and floating combat numbers (SRP).
/// </summary>
internal sealed class WorldEntityOverlayRenderer
{
    private const float NameplateRadiusSq = 380f * 380f;

    public void DrawGeometry(
        GeometryBatch batch,
        IWorldReadModel world,
        IVectorArtRepository? art,
        CombatFeedbackTracker feedback,
        CameraRig2D cameraRig,
        ReadOnlySpan<DamagedPropHudMarker> damagedProps,
        BitmapFont font,
        int viewportWidth,
        int viewportHeight,
        float animTime)
    {
        Vector2 playerPos = world.LocalPlayerInterpolatedPosition;

        // 1. Prop durability bars (46x6px beveled with smooth fade-out)
        for (int i = 0; i < damagedProps.Length; i++)
        {
            ref readonly DamagedPropHudMarker pm = ref damagedProps[i];
            if (pm.Alpha <= 0.02f)
            {
                continue;
            }

            Vector2 pmWorld = pm.WorldPos;
            Vector2 screenPt = cameraRig.Camera.WorldToScreen(in pmWorld);
            if (screenPt.X < -40f || screenPt.X > viewportWidth + 40f || screenPt.Y < -20f || screenPt.Y > viewportHeight + 20f)
            {
                continue;
            }

            byte aBg = (byte)Math.Clamp(pm.Alpha * 235f, 0f, 235f);
            byte aFill = (byte)Math.Clamp(pm.Alpha * 255f, 0f, 255f);
            float frac = Math.Clamp(pm.Current / MathF.Max(1f, pm.Max), 0f, 1f);
            const float barW = 46f;
            const float barH = 6f;
            float bx = MathF.Floor(screenPt.X - (barW * 0.5f));
            float by = MathF.Floor(screenPt.Y);
            batch.FillRectangle(new RectangleF(bx - 1f, by - 1f, barW + 2f, barH + 2f), new ColorRgba(8, 12, 20, aBg));
            ColorRgba fill = pm.IsMineral
                ? new ColorRgba(245, 158, 11, aFill)
                : new ColorRgba(74, 222, 128, aFill);
            if (frac > 0.01f)
            {
                batch.FillRectangle(new RectangleF(bx, by, barW * frac, barH), fill);
                batch.FillRectangle(new RectangleF(bx, by, barW * frac, 1.5f), new ColorRgba(255, 255, 255, (byte)(pm.Alpha * 70f)));
            }
        }

        // 2. Monster / NPC / Remote Player proportional overhead plates & ground loot labels
        IReadOnlyList<RemoteEntityView> remotes = world.RemoteEntities;
        for (int i = 0; i < remotes.Count; i++)
        {
            RemoteEntityView r = remotes[i];
            if (r.Kind == EntityKind.Projectile)
            {
                continue;
            }

            float distSq = Vector2.DistanceSquared(playerPos, r.Position);
            if (r.Kind == EntityKind.LootDrop)
            {
                if (distSq <= (175f * 175f) && !string.IsNullOrEmpty(r.Name))
                {
                    Vector2 lootWorld = r.Position;
                    Vector2 lootScreen = cameraRig.Camera.WorldToScreen(in lootWorld);
                    string lootLabel = FormatLootLabel(in r);
                    TextMeasureResult lSize = font.MeasureString(lootLabel, TextLayoutOptions.Default, 1f);
                    float lpW = lSize.Width + 10f;
                    batch.FillRectangle(new RectangleF(MathF.Floor(lootScreen.X - (lpW * 0.5f)), MathF.Floor(lootScreen.Y - 30f), lpW, 18f), new ColorRgba(10, 13, 20, 200));
                }

                continue;
            }

            if (distSq > NameplateRadiusSq)
            {
                continue;
            }

            Vector2 crownWorld = ResolveHeadCrownWorld(in r, art, world.Races.Current);
            Vector2 screenPt = cameraRig.Camera.WorldToScreen(in crownWorld);
            if (screenPt.X < -90f || screenPt.X > viewportWidth + 90f || screenPt.Y < -70f || screenPt.Y > viewportHeight + 70f)
            {
                continue;
            }

            float barTopY = MathF.Floor(screenPt.Y - 10f);
            bool showBar = r.MaxHealth > 0f && (r.Kind == EntityKind.Monster || r.CurrentHealth < r.MaxHealth - 0.5f);

            if (showBar)
            {
                float barW = r.MaxHealth >= 120f ? 76f : 68f;
                const float barH = 9f;
                float bx = MathF.Floor(screenPt.X - (barW * 0.5f));
                float ratio = Math.Clamp(r.CurrentHealth / r.MaxHealth, 0f, 1f);
                float ghostRatio = feedback.GetEntityGhostRatio(r.NetworkId, r.CurrentHealth, r.MaxHealth);

                bool isAttacking = r.AttackAnimRemaining > 0f;
                ColorRgba outerBorder = isAttacking
                    ? new ColorRgba(248, 113, 113, (byte)(180 + (75 * MathF.Abs(MathF.Sin(animTime * 18f)))))
                    : new ColorRgba(8, 12, 20, 240);

                batch.FillRectangle(new RectangleF(bx - 1.5f, barTopY - 1.5f, barW + 3f, barH + 3f), outerBorder);
                batch.FillRectangle(new RectangleF(bx, barTopY, barW, barH), new ColorRgba(22, 30, 46, 240));

                // White-amber ghost damage trail smoothly catching up to actual HP
                float ghostW = barW * ghostRatio;
                if (ghostW > 0.5f)
                {
                    batch.FillRectangle(new RectangleF(bx, barTopY, ghostW, barH), new ColorRgba(253, 224, 71, 215));
                }

                ColorRgba fillColor = r.Kind == EntityKind.Player
                    ? new ColorRgba(34, 197, 94, 255)
                    : r.Kind == EntityKind.Npc
                        ? new ColorRgba(234, 179, 8, 255)
                        : ratio > 0.45f
                            ? new ColorRgba(225, 29, 72, 255)
                            : new ColorRgba(249, 115, 22, 255);
                float fillW = barW * ratio;
                if (fillW > 0.5f)
                {
                    batch.FillRectangle(new RectangleF(bx, barTopY, fillW, barH), fillColor);
                    batch.FillRectangle(new RectangleF(bx, barTopY, fillW, 2.2f), new ColorRgba(255, 255, 255, 85));
                }

                for (int seg = 1; seg <= 3; seg++)
                {
                    float tx = MathF.Floor(bx + (barW * (seg * 0.25f)));
                    batch.FillRectangle(new RectangleF(tx, barTopY, 1f, barH), new ColorRgba(8, 12, 20, 150));
                }

                // Rank / category diamond pip anchored neatly left of the bar
                Vector2 pipCenter = new(bx - 6f, barTopY + (barH * 0.5f));
                DrawRankDiamond(batch, pipCenter, ResolveEntityAccent(in r, art));
            }

            if (!string.IsNullOrEmpty(r.Name))
            {
                string displayName = FormatDisplayTitle(in r, distSq);
                TextMeasureResult measure = font.MeasureString(displayName, TextLayoutOptions.Default, 1f);
                float pillW = measure.Width + 12f;
                float pillX = MathF.Floor(screenPt.X - (pillW * 0.5f));
                float pillY = showBar ? barTopY - 21f : MathF.Floor(screenPt.Y - 19f);
                batch.FillRectangle(new RectangleF(pillX, pillY, pillW, 18f), new ColorRgba(10, 13, 20, 185));
            }
        }
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        IVectorArtRepository? art,
        CombatFeedbackTracker feedback,
        CameraRig2D cameraRig,
        int viewportWidth,
        int viewportHeight,
        bool showCombatNumbers = true)
    {
        ColorRgba outline = new(10, 12, 18, 255);
        Vector2 playerPos = world.LocalPlayerInterpolatedPosition;
        IReadOnlyList<RemoteEntityView> remotes = world.RemoteEntities;

        for (int i = 0; i < remotes.Count; i++)
        {
            RemoteEntityView r = remotes[i];
            if (r.Kind == EntityKind.Projectile)
            {
                continue;
            }

            float distSq = Vector2.DistanceSquared(playerPos, r.Position);
            if (r.Kind == EntityKind.LootDrop)
            {
                if (!string.IsNullOrEmpty(r.Name) && distSq <= (175f * 175f))
                {
                    Vector2 lootWorld = r.Position;
                    Vector2 lootScreen = cameraRig.Camera.WorldToScreen(in lootWorld);
                    string lootLabel = FormatLootLabel(in r);
                    TextMeasureResult lSize = font.MeasureString(lootLabel, TextLayoutOptions.Default, 1f);
                    ColorRgba lootCol = r.WeaponUpgradeLevel > 0 ? new ColorRgba(34, 211, 238, 255) : new ColorRgba(253, 224, 71, 255);
                    spriteBatch.DrawOutlinedString(font, lootLabel, new Vector2(MathF.Floor(lootScreen.X - (lSize.Width * 0.5f)), MathF.Floor(lootScreen.Y - 29f)), lootCol, outline, 1, 1f);
                }

                continue;
            }

            if (string.IsNullOrEmpty(r.Name) || distSq > NameplateRadiusSq)
            {
                continue;
            }

            Vector2 crownWorld = ResolveHeadCrownWorld(in r, art, world.Races.Current);
            Vector2 screenPt = cameraRig.Camera.WorldToScreen(in crownWorld);
            if (screenPt.X < -90f || screenPt.X > viewportWidth + 90f || screenPt.Y < -70f || screenPt.Y > viewportHeight + 70f)
            {
                continue;
            }

            bool showBar = r.MaxHealth > 0f && (r.Kind == EntityKind.Monster || r.CurrentHealth < r.MaxHealth - 0.5f);
            float nameY = showBar
                ? MathF.Floor(screenPt.Y - 30f)
                : MathF.Floor(screenPt.Y - 18f);

            ColorRgba labelColor = r.Kind == EntityKind.Monster
                ? new ColorRgba(254, 215, 215, 255)
                : r.Kind == EntityKind.Npc
                    ? new ColorRgba(253, 224, 71, 255)
                    : new ColorRgba(110, 231, 183, 255);
            string displayName = FormatDisplayTitle(in r, distSq);
            TextMeasureResult measure = font.MeasureString(displayName, TextLayoutOptions.Default, 1f);
            Vector2 labelPos = new(MathF.Floor(screenPt.X - (measure.Width * 0.5f)), nameY);
            spriteBatch.DrawOutlinedString(font, displayName, labelPos, labelColor, outline, 1, 1f);
        }

        if (!showCombatNumbers)
        {
            return;
        }

        IReadOnlyList<FloatingCombatText> floating = feedback.FloatingTexts;
        for (int i = 0; i < floating.Count; i++)
        {
            FloatingCombatText ft = floating[i];
            Vector2 ftWorld = ft.Position;
            Vector2 screenFt = cameraRig.Camera.WorldToScreen(in ftWorld);
            byte alpha = (byte)(Math.Clamp(ft.RemainingSeconds / 0.95f, 0f, 1f) * 255f);
            ColorRgba tint = new(ft.Color.R, ft.Color.G, ft.Color.B, alpha);
            ColorRgba fadeOutline = new(10, 12, 18, alpha);
            TextMeasureResult ftSize = font.MeasureString(ft.Text, TextLayoutOptions.Default, ft.Scale);
            Vector2 pos = new(MathF.Floor(screenFt.X - (ftSize.Width * 0.5f)), MathF.Floor(screenFt.Y));
            spriteBatch.DrawOutlinedString(font, ft.Text, pos, tint, fadeOutline, 1, ft.Scale);
        }
    }

    private static Vector2 ResolveHeadCrownWorld(in RemoteEntityView remote, IVectorArtRepository? art, RaceCatalog? races)
    {
        float radius = remote.Radius > 0f ? remote.Radius : 7f;
        float footY = remote.Position.Y + radius;
        float worldHeight = MathF.Max(28f, radius * 3.8f);

        if (art is not null)
        {
            if (remote.Kind == EntityKind.Monster && art.TryGetMonster(remote.ArchetypeId, out CompiledVectorRig? mr))
            {
                worldHeight = mr.WorldHeight;
            }
            else if (remote.Kind == EntityKind.Npc && art.TryGetNpc(remote.ArchetypeId, out CompiledVectorRig? nr))
            {
                worldHeight = nr.WorldHeight;
            }
            else if (remote.Kind == EntityKind.Player && CharacterLookResolver.TryResolve(art, races, remote.Look, out CompiledVectorRig? cr, out _) && cr is not null)
            {
                worldHeight = cr.WorldHeight;
            }
        }

        return new Vector2(remote.Position.X, footY - worldHeight - 4f);
    }

    private static ColorRgba ResolveEntityAccent(in RemoteEntityView remote, IVectorArtRepository? art)
    {
        if (art is not null)
        {
            if (remote.Kind == EntityKind.Monster && art.TryGetMonster(remote.ArchetypeId, out CompiledVectorRig? mr))
            {
                return mr.AccentColor;
            }

            if (remote.Kind == EntityKind.Npc && art.TryGetNpc(remote.ArchetypeId, out CompiledVectorRig? nr))
            {
                return nr.AccentColor;
            }
        }

        return remote.Kind == EntityKind.Npc
            ? new ColorRgba(250, 204, 21, 255)
            : new ColorRgba(248, 113, 113, 255);
    }

    private static void DrawRankDiamond(GeometryBatch batch, Vector2 center, ColorRgba fill)
    {
        Span<Vector2> outer =
        [
            new(center.X, center.Y - 4.5f),
            new(center.X + 4.5f, center.Y),
            new(center.X, center.Y + 4.5f),
            new(center.X - 4.5f, center.Y),
        ];
        batch.FillConvexPolygon(outer, new ColorRgba(10, 14, 24, 245));

        Span<Vector2> inner =
        [
            new(center.X, center.Y - 3.0f),
            new(center.X + 3.0f, center.Y),
            new(center.X, center.Y + 3.0f),
            new(center.X - 3.0f, center.Y),
        ];
        batch.FillConvexPolygon(inner, fill);
    }

    private static string FormatDisplayTitle(in RemoteEntityView r, float distSq)
    {
        // Near a forge artisan (the village blacksmith too), U opens the forge (ADR-024). What E does is shown by the
        // interact prompt over its one target, not on name tags (InteractionController).
        if (r.Kind == EntityKind.Npc && distSq <= (175f * 175f) &&
            (WorldActorCatalog.GetNpcSpec(r.ArchetypeId).Capabilities & NpcCapabilities.ForgeArtisan) != 0)
        {
            return GameText.Format("hud.nameplate.forge", new TextArg("name", r.Name));
        }

        return (r.Kind is EntityKind.Monster or EntityKind.Player) && r.Level > 0
            ? GameText.Format("hud.nameplate.level", new TextArg("level", r.Level), new TextArg("name", r.Name))
            : r.Name;
    }

    private static string FormatLootLabel(in RemoteEntityView r)
    {
        if (r.WeaponUpgradeLevel > 0)
        {
            return $"+{r.WeaponUpgradeLevel} {r.Name}";
        }

        return r.LootCount > 1 ? $"{r.Name} x{r.LootCount}" : r.Name;
    }
}

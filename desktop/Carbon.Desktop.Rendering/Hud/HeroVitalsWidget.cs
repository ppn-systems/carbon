// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Biomes;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Top-left RPG hero unit frame displaying a live <c>.cvg.json</c> vector portrait box, class title badge,
/// transport pill, smooth ghost-damage HP bar, swing readiness gauge, and biome/telemetry strip (SRP).
/// </summary>
internal sealed class HeroVitalsWidget
{
    public void DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        CombatFeedbackTracker feedback,
        float animTime)
    {
        RectangleF card = new(14f, 14f, 412f, 118f);
        batch.FillRectangle(card, new ColorRgba(10, 15, 28, 228));
        batch.DrawRectangle(card, new ColorRgba(71, 85, 105, 235), 1.5f);
        batch.FillRectangle(new RectangleF(16f, 16f, 408f, 2f), new ColorRgba(56, 189, 248, 70));

        // 58x58 portrait frame rendering auto-framed live declarative hero rig
        RectangleF portraitBox = new(24f, 24f, 58f, 58f);
        batch.FillRectangle(portraitBox, new ColorRgba(15, 23, 42, 245));
        ColorRgba accent = new(56, 189, 248, 255);
        ItemSlot activeSlot = world.Inventory.GetSlot(world.SelectedHotbarSlot);
        CompiledItemVectorArt? equippedArt = null;
        if (!activeSlot.IsEmpty && art is not null)
        {
            _ = art.TryGetItem(activeSlot.ItemId, out equippedArt);
        }

        if (art is not null && art.TryGetCharacter(world.LocalPlayerAppearancePreset, out CompiledVectorRig? rig))
        {
            accent = rig.AccentColor;
            VectorRigRenderer.DrawPortraitRig(
                batch,
                rig,
                new Vector2(53f, 53f),
                boxSize: 52f,
                Direction8.South,
                "idle",
                animTime,
                equippedArt);
        }

        batch.DrawRectangle(portraitBox, accent, 2f);

        // Mini active-tool medallion anchored at portrait bottom-right
        if (equippedArt is not null)
        {
            bool wornOut = ToolUsagePolicy.IsWornOut(in activeSlot);
            RectangleF miniBox = new(64f, 64f, 22f, 22f);
            batch.FillRectangle(miniBox, new ColorRgba(10, 15, 28, 245));
            VectorRigRenderer.DrawItemIcon(batch, equippedArt, new Vector2(75f, 75f), 15f);
            batch.DrawRectangle(miniBox, wornOut ? new ColorRgba(239, 68, 68, 255) : equippedArt.AccentColor, 1.3f);
        }

        // Class accent header strip & transport status badge
        ColorRgba badgeColor = world.IsUdpActive
            ? new ColorRgba(16, 185, 129, 235)
            : (world.IsConnected ? new ColorRgba(37, 99, 235, 235) : new ColorRgba(185, 28, 28, 235));
        batch.FillRectangle(new RectangleF(320f, 22f, 94f, 19f), badgeColor);

        // Dual-layer beveled HP bar with smooth ghost trail
        const float barX = 94f;
        const float barY = 48f;
        const float barW = 320f;
        const float barH = 22f;
        float maxHp = MathF.Max(1f, world.LocalPlayerMaxHealth);
        float ratio = Math.Clamp(world.LocalPlayerHealth / maxHp, 0f, 1f);
        float ghost = Math.Clamp(feedback.LocalGhostHpRatio, ratio, 1f);

        batch.FillRectangle(new RectangleF(barX - 1f, barY - 1f, barW + 2f, barH + 2f), new ColorRgba(6, 9, 16, 245));
        batch.FillRectangle(new RectangleF(barX, barY, barW, barH), new ColorRgba(26, 36, 54, 240));
        if (ghost > ratio + 0.004f)
        {
            batch.FillRectangle(new RectangleF(barX, barY, barW * ghost, barH), new ColorRgba(253, 224, 71, 220));
        }

        float fillW = barW * ratio;
        if (fillW > 0.5f)
        {
            batch.FillRectangle(new RectangleF(barX, barY, fillW, barH), new ColorRgba(220, 38, 38, 255));
            batch.FillRectangle(new RectangleF(barX, barY, fillW, 5f), new ColorRgba(255, 255, 255, 65));
        }

        for (int seg = 1; seg <= 3; seg++)
        {
            float tx = MathF.Floor(barX + (barW * (seg * 0.25f)));
            batch.FillRectangle(new RectangleF(tx, barY, 1f, barH), new ColorRgba(8, 12, 20, 145));
        }

        // Action / swing readiness strip
        float readyFrac = world.CurrentAction.IsActive ? 1f - world.CurrentAction.Progress : 1f;
        batch.FillRectangle(new RectangleF(barX, 74f, barW, 5f), new ColorRgba(15, 23, 42, 235));
        batch.FillRectangle(new RectangleF(barX, 74f, barW * readyFrac, 5f), new ColorRgba(56, 189, 248, 225));
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int ptx,
        int pty,
        BiomeType biome,
        uint activeItemId,
        float hourOfDay,
        float fps)
    {
        ColorRgba black = new(10, 12, 18, 255);
        int curHp = (int)MathF.Ceiling(world.LocalPlayerHealth);
        int maxHp = (int)MathF.Ceiling(world.LocalPlayerMaxHealth);

        string heroName = string.IsNullOrWhiteSpace(world.ActiveCharacterName)
            ? "CARBON HERO"
            : world.ActiveCharacterName.ToUpperInvariant();
        if (heroName.Length > 11)
        {
            heroName = heroName[..11];
        }

        string classTitle = art is not null && art.TryGetCharacter(world.LocalPlayerAppearancePreset, out CompiledVectorRig? rig)
            ? rig.Id.ToUpperInvariant()
            : "HERO";

        spriteBatch.DrawOutlinedString(font, $"{heroName} [{classTitle}]", new Vector2(94f, 24f), ColorRgba.White, black, 1, 1.5f);

        string badgeText = world.TransportLabel;
        TextMeasureResult badgeSize = font.MeasureString(badgeText, TextLayoutOptions.Default, 1.5f);
        float badgeX = MathF.Floor(320f + ((94f - badgeSize.Width) * 0.5f));
        spriteBatch.DrawOutlinedString(font, badgeText, new Vector2(badgeX, 25f), ColorRgba.White, black, 1, 1.5f);

        string hpText = $"HP {curHp}/{maxHp}";
        TextMeasureResult hpSize = font.MeasureString(hpText, TextLayoutOptions.Default, 2f);
        float hpX = MathF.Floor(94f + ((320f - hpSize.Width) * 0.5f));
        spriteBatch.DrawOutlinedString(font, hpText, new Vector2(hpX, 52f), ColorRgba.White, black, 1, 2f);

        int h = (int)MathF.Floor(hourOfDay) % 24;
        int m = (int)MathF.Floor((hourOfDay - MathF.Floor(hourOfDay)) * 60f);
        ItemSlot activeSlot = world.Inventory.GetSlot(world.SelectedHotbarSlot);
        bool wornOut = ToolUsagePolicy.IsWornOut(in activeSlot);
        string itemName = StarterGameCatalog.GetDisplayName(activeItemId);
        string itemLabel = wornOut ? $"{itemName} [WORN]" : itemName;
        ColorRgba itemColor = wornOut ? new ColorRgba(248, 113, 113, 255) : new ColorRgba(226, 232, 240, 255);
        spriteBatch.DrawOutlinedString(font, $"{itemLabel} | {biome}", new Vector2(94f, 84f), itemColor, black, 1, 1.5f);
        spriteBatch.DrawOutlinedString(font, $"Pos ({ptx},{pty}) | {h:D2}:{m:D2} | {(int)fps} FPS", new Vector2(24f, 106f), new ColorRgba(148, 163, 184, 255), black, 1, 1.5f);
    }
}

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
        RectangleF card = new(14f, 14f, 424f, 142f);
        batch.FillRectangle(card, new ColorRgba(10, 15, 28, 230));
        batch.DrawRectangle(card, new ColorRgba(71, 85, 105, 235), 1.5f);
        batch.FillRectangle(new RectangleF(16f, 16f, 420f, 2f), new ColorRgba(56, 189, 248, 75));

        // 58x58 portrait frame rendering auto-framed live armored hero rig
        RectangleF portraitBox = new(24f, 24f, 58f, 58f);
        batch.FillRectangle(portraitBox, new ColorRgba(15, 23, 42, 245));
        ColorRgba accent = new(56, 189, 248, 255);
        InventoryContainer inv = world.Inventory;
        ItemSlot activeSlot = inv.GetSlot(world.SelectedHotbarSlot);
        CompiledItemVectorArt? equippedArt = null;
        if (!activeSlot.IsEmpty && art is not null)
        {
            _ = art.TryGetItem(activeSlot.ItemId, out equippedArt);
        }

        if (art is not null && art.TryGetCharacter(world.LocalPlayerAppearancePreset, out CompiledVectorRig? rig))
        {
            accent = rig.AccentColor;
            ushort headId = PlayerInventoryLayout.HeadSlot < inv.Capacity ? (ushort)inv.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId : (ushort)0;
            ushort chestId = PlayerInventoryLayout.ChestSlot < inv.Capacity ? (ushort)inv.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId : (ushort)0;
            ushort legsId = PlayerInventoryLayout.LegsSlot < inv.Capacity ? (ushort)inv.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId : (ushort)0;
            ushort feetId = PlayerInventoryLayout.FeetSlot < inv.Capacity ? (ushort)inv.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId : (ushort)0;
            VisualEquipmentLoadout loadout = new(headId, chestId, legsId, feetId, (ushort)activeSlot.ItemId, ItemEnhancementPolicy.GetUpgradeLevel(in activeSlot));
            VectorRigRenderer.DrawPortraitRig(
                batch,
                rig,
                new Vector2(53f, 53f),
                boxSize: 52f,
                Direction8.South,
                "idle",
                animTime,
                equippedArt,
                world.LocalPlayerAppearancePreset,
                loadout);
        }

        batch.DrawRectangle(portraitBox, accent, 2f);

        // Level badge anchored at portrait top-left & tool medallion at bottom-right
        batch.FillRectangle(new RectangleF(24f, 86f, 58f, 16f), new ColorRgba(30, 41, 59, 245));
        batch.DrawRectangle(new RectangleF(24f, 86f, 58f, 16f), new ColorRgba(250, 204, 21, 220), 1f);

        if (equippedArt is not null)
        {
            bool wornOut = ToolUsagePolicy.IsWornOut(in activeSlot);
            RectangleF miniBox = new(64f, 64f, 22f, 22f);
            batch.FillRectangle(miniBox, new ColorRgba(10, 15, 28, 245));
            VectorRigRenderer.DrawItemIcon(batch, equippedArt, new Vector2(75f, 75f), 15f);
            batch.DrawRectangle(miniBox, wornOut ? new ColorRgba(239, 68, 68, 255) : equippedArt.AccentColor, 1.3f);
        }

        // Transport status pill & DEF shield badge
        ColorRgba badgeColor = world.IsUdpActive
            ? new ColorRgba(16, 185, 129, 235)
            : (world.IsConnected ? new ColorRgba(37, 99, 235, 235) : new ColorRgba(185, 28, 28, 235));
        batch.FillRectangle(new RectangleF(334f, 22f, 94f, 18f), badgeColor);

        // Bars layout (HP, MP, Stamina, XP)
        const float barX = 94f;
        const float barW = 334f;

        // 1. HP bar (y=44, h=18)
        float maxHp = MathF.Max(1f, world.LocalPlayerMaxHealth);
        float ratio = Math.Clamp(world.LocalPlayerHealth / maxHp, 0f, 1f);
        float ghost = Math.Clamp(feedback.LocalGhostHpRatio, ratio, 1f);
        batch.FillRectangle(new RectangleF(barX - 1f, 43f, barW + 2f, 20f), new ColorRgba(6, 9, 16, 245));
        batch.FillRectangle(new RectangleF(barX, 44f, barW, 18f), new ColorRgba(26, 36, 54, 240));
        if (ghost > ratio + 0.004f)
        {
            batch.FillRectangle(new RectangleF(barX, 44f, barW * ghost, 18f), new ColorRgba(253, 224, 71, 220));
        }

        float fillW = barW * ratio;
        if (fillW > 0.5f)
        {
            batch.FillRectangle(new RectangleF(barX, 44f, fillW, 18f), new ColorRgba(220, 38, 38, 255));
            batch.FillRectangle(new RectangleF(barX, 44f, fillW, 4f), new ColorRgba(255, 255, 255, 65));
        }

        // 2. MP bar (y=66, h=13)
        float mpRatio = Math.Clamp(world.LocalPlayerMana / MathF.Max(1f, world.LocalPlayerMaxMana), 0f, 1f);
        batch.FillRectangle(new RectangleF(barX - 1f, 65f, barW + 2f, 15f), new ColorRgba(6, 9, 16, 240));
        batch.FillRectangle(new RectangleF(barX, 66f, barW, 13f), new ColorRgba(18, 28, 48, 240));
        if (mpRatio > 0.005f)
        {
            batch.FillRectangle(new RectangleF(barX, 66f, barW * mpRatio, 13f), new ColorRgba(37, 99, 235, 255));
            batch.FillRectangle(new RectangleF(barX, 66f, barW * mpRatio, 3f), new ColorRgba(125, 211, 252, 90));
        }

        // 3. Stamina bar (y=83, h=11)
        float staRatio = Math.Clamp(world.LocalPlayerStamina / MathF.Max(1f, world.LocalPlayerMaxStamina), 0f, 1f);
        batch.FillRectangle(new RectangleF(barX - 1f, 82f, barW + 2f, 13f), new ColorRgba(6, 9, 16, 240));
        batch.FillRectangle(new RectangleF(barX, 83f, barW, 11f), new ColorRgba(18, 28, 48, 240));
        if (staRatio > 0.005f)
        {
            ColorRgba staCol = staRatio > 0.25f ? new ColorRgba(16, 185, 129, 255) : new ColorRgba(245, 158, 11, 255);
            batch.FillRectangle(new RectangleF(barX, 83f, barW * staRatio, 11f), staCol);
            batch.FillRectangle(new RectangleF(barX, 83f, barW * staRatio, 2.5f), new ColorRgba(255, 255, 255, 70));
        }

        // 4. XP progression strip (y=98, h=6)
        float xpRatio = Math.Clamp(world.LocalPlayerExperience / MathF.Max(1f, world.LocalPlayerXpToNext), 0f, 1f);
        batch.FillRectangle(new RectangleF(barX, 98f, barW, 6f), new ColorRgba(15, 23, 42, 240));
        if (xpRatio > 0.004f)
        {
            batch.FillRectangle(new RectangleF(barX, 98f, barW * xpRatio, 6f), new ColorRgba(168, 85, 247, 255));
        }
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
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
            ? "HERO"
            : world.ActiveCharacterName.ToUpperInvariant();
        if (heroName.Length > 10)
        {
            heroName = heroName[..10];
        }

        spriteBatch.DrawOutlinedString(font, $"{heroName} | DEF {(int)MathF.Round(world.LocalPlayerDefense)}", new Vector2(94f, 24f), ColorRgba.White, black, 1, 1.5f);

        string lvText = $"LV.{world.LocalPlayerLevel}";
        TextMeasureResult lvSize = font.MeasureString(lvText, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, lvText, new Vector2(MathF.Floor(24f + ((58f - lvSize.Width) * 0.5f)), 89f), new ColorRgba(250, 204, 21, 255), black, 1, 1f);

        string badgeText = world.TransportLabel;
        TextMeasureResult badgeSize = font.MeasureString(badgeText, TextLayoutOptions.Default, 1.5f);
        float badgeX = MathF.Floor(334f + ((94f - badgeSize.Width) * 0.5f));
        spriteBatch.DrawOutlinedString(font, badgeText, new Vector2(badgeX, 24f), ColorRgba.White, black, 1, 1.5f);

        string hpText = $"HP {curHp}/{maxHp}";
        TextMeasureResult hpSize = font.MeasureString(hpText, TextLayoutOptions.Default, 1.5f);
        spriteBatch.DrawOutlinedString(font, hpText, new Vector2(MathF.Floor(94f + ((334f - hpSize.Width) * 0.5f)), 46f), ColorRgba.White, black, 1, 1.5f);

        string mpText = $"MP {(int)MathF.Floor(world.LocalPlayerMana)}/{(int)MathF.Ceiling(world.LocalPlayerMaxMana)}";
        TextMeasureResult mpSize = font.MeasureString(mpText, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, mpText, new Vector2(MathF.Floor(94f + ((334f - mpSize.Width) * 0.5f)), 67f), new ColorRgba(224, 242, 254, 255), black, 1, 1f);

        string staText = $"STA {(int)MathF.Floor(world.LocalPlayerStamina)}/{(int)MathF.Ceiling(world.LocalPlayerMaxStamina)}";
        TextMeasureResult staSize = font.MeasureString(staText, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, staText, new Vector2(MathF.Floor(94f + ((334f - staSize.Width) * 0.5f)), 84f), new ColorRgba(209, 250, 229, 255), black, 1, 1f);

        int h = (int)MathF.Floor(hourOfDay) % 24;
        int m = (int)MathF.Floor((hourOfDay - MathF.Floor(hourOfDay)) * 60f);
        ItemSlot activeSlot = world.Inventory.GetSlot(world.SelectedHotbarSlot);
        bool wornOut = ToolUsagePolicy.IsWornOut(in activeSlot);
        string itemName = StarterGameCatalog.GetDisplayName(activeItemId);
        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in activeSlot);
        string upgTag = upg > 0 ? $"+{upg} " : string.Empty;
        string itemLabel = wornOut ? $"{upgTag}{itemName} [WORN]" : $"{upgTag}{itemName}";
        ColorRgba itemColor = wornOut ? new ColorRgba(248, 113, 113, 255) : new ColorRgba(226, 232, 240, 255);
        spriteBatch.DrawOutlinedString(font, $"{itemLabel} | XP {world.LocalPlayerExperience}/{world.LocalPlayerXpToNext}", new Vector2(24f, 111f), itemColor, black, 1, 1.5f);
        spriteBatch.DrawOutlinedString(font, $"{biome} ({ptx},{pty}) | {h:D2}:{m:D2} | {(int)fps} FPS", new Vector2(24f, 130f), new ColorRgba(148, 163, 184, 255), black, 1, 1.5f);
    }
}

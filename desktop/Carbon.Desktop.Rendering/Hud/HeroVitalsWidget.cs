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
/// Top-left RPG hero unit frame displaying a live <c>.cvg.json</c> vector portrait box, level &amp; defense badge,
/// transport pill, smooth ghost-damage HP bar, MP/Stamina/XP strips, and biome/telemetry readout (SRP).
/// </summary>
internal sealed class HeroVitalsWidget
{
    private const float CardX = 16f;
    private const float CardY = 14f;
    private const float CardW = 344f;
    private const float CardH = 118f;

    public void DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        CombatFeedbackTracker? feedback,
        float animTime)
    {
        RectangleF card = new(CardX, CardY, CardW, CardH);
        batch.FillRectangle(card, new ColorRgba(10, 15, 28, 232));
        batch.DrawRectangle(card, new ColorRgba(71, 85, 105, 235), 1.5f);
        batch.FillRectangle(new RectangleF(CardX + 2f, CardY + 2f, CardW - 4f, 2f), new ColorRgba(56, 189, 248, 75));

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        ItemSlot activeSlot = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;

        // 52x52 portrait frame rendering auto-framed live armored hero rig
        RectangleF portraitBox = new(CardX + 8f, CardY + 8f, 52f, 52f);
        batch.FillRectangle(portraitBox, new ColorRgba(15, 23, 42, 245));
        ColorRgba accent = new(56, 189, 248, 255);
        CompiledItemVectorArt? equippedArt = null;
        if (!activeSlot.IsEmpty && art is not null)
        {
            _ = art.TryGetItem(activeSlot.ItemId, out equippedArt);
        }

        if (art is not null && art.TryGetCharacter(world.LocalPlayerAppearancePreset, out CompiledVectorRig? rig))
        {
            accent = rig.AccentColor;
            ushort headId = PlayerInventoryLayout.HeadSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.HeadSlot].ItemId : (ushort)0;
            ushort chestId = PlayerInventoryLayout.ChestSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.ChestSlot].ItemId : (ushort)0;
            ushort legsId = PlayerInventoryLayout.LegsSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.LegsSlot].ItemId : (ushort)0;
            ushort feetId = PlayerInventoryLayout.FeetSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.FeetSlot].ItemId : (ushort)0;
            VisualEquipmentLoadout loadout = new(headId, chestId, legsId, feetId, (ushort)activeSlot.ItemId, ItemEnhancementPolicy.GetUpgradeLevel(in activeSlot));
            VectorRigRenderer.DrawPortraitRig(
                batch,
                rig,
                new Vector2(portraitBox.X + 26f, portraitBox.Y + 26f),
                boxSize: 46f,
                Direction8.South,
                "idle",
                animTime,
                equippedArt,
                world.LocalPlayerAppearancePreset,
                loadout,
                art);
        }

        batch.DrawRectangle(portraitBox, accent, 1.8f);

        // Level badge anchored below portrait
        batch.FillRectangle(new RectangleF(portraitBox.X, portraitBox.Bottom + 3f, 52f, 15f), new ColorRgba(30, 41, 59, 245));
        batch.DrawRectangle(new RectangleF(portraitBox.X, portraitBox.Bottom + 3f, 52f, 15f), new ColorRgba(250, 204, 21, 220), 1f);

        if (equippedArt is not null)
        {
            bool wornOut = ToolUsagePolicy.IsWornOut(in activeSlot);
            RectangleF miniBox = new(portraitBox.Right - 18f, portraitBox.Bottom - 18f, 20f, 20f);
            batch.FillRectangle(miniBox, new ColorRgba(10, 15, 28, 245));
            VectorRigRenderer.DrawItemIcon(batch, equippedArt, new Vector2(miniBox.X + 10f, miniBox.Y + 10f), 14f);
            batch.DrawRectangle(miniBox, wornOut ? new ColorRgba(239, 68, 68, 255) : equippedArt.AccentColor, 1.2f);
        }

        // Transport status pill
        ColorRgba badgeColor = world.IsUdpActive
            ? new ColorRgba(16, 185, 129, 235)
            : (world.IsConnected ? new ColorRgba(37, 99, 235, 235) : new ColorRgba(185, 28, 28, 235));
        batch.FillRectangle(new RectangleF(CardX + CardW - 78f, CardY + 7f, 70f, 15f), badgeColor);

        const float barX = CardX + 68f;
        const float barW = CardW - 76f;

        // 1. HP bar (y=36, h=16)
        float maxHp = MathF.Max(1f, world.LocalPlayerMaxHealth);
        float ratio = Math.Clamp(world.LocalPlayerHealth / maxHp, 0f, 1f);
        float ghost = feedback is not null ? Math.Clamp(feedback.LocalGhostHpRatio, ratio, 1f) : ratio;
        batch.FillRectangle(new RectangleF(barX - 1f, CardY + 24f, barW + 2f, 18f), new ColorRgba(6, 9, 16, 245));
        batch.FillRectangle(new RectangleF(barX, CardY + 25f, barW, 16f), new ColorRgba(26, 36, 54, 240));
        if (ghost > ratio + 0.004f)
        {
            batch.FillRectangle(new RectangleF(barX, CardY + 25f, barW * ghost, 16f), new ColorRgba(253, 224, 71, 220));
        }

        float fillW = barW * ratio;
        if (fillW > 0.5f)
        {
            batch.FillRectangle(new RectangleF(barX, CardY + 25f, fillW, 16f), new ColorRgba(220, 38, 38, 255));
            batch.FillRectangle(new RectangleF(barX, CardY + 25f, fillW, 3.5f), new ColorRgba(255, 255, 255, 65));
        }

        // 2. MP bar (h=12)
        float mpRatio = Math.Clamp(world.LocalPlayerMana / MathF.Max(1f, world.LocalPlayerMaxMana), 0f, 1f);
        batch.FillRectangle(new RectangleF(barX - 1f, CardY + 44f, barW + 2f, 14f), new ColorRgba(6, 9, 16, 240));
        batch.FillRectangle(new RectangleF(barX, CardY + 45f, barW, 12f), new ColorRgba(18, 28, 48, 240));
        if (mpRatio > 0.005f)
        {
            batch.FillRectangle(new RectangleF(barX, CardY + 45f, barW * mpRatio, 12f), new ColorRgba(37, 99, 235, 255));
            batch.FillRectangle(new RectangleF(barX, CardY + 45f, barW * mpRatio, 2.5f), new ColorRgba(125, 211, 252, 90));
        }

        // 3. Stamina bar (h=10)
        float staRatio = Math.Clamp(world.LocalPlayerStamina / MathF.Max(1f, world.LocalPlayerMaxStamina), 0f, 1f);
        batch.FillRectangle(new RectangleF(barX - 1f, CardY + 60f, barW + 2f, 12f), new ColorRgba(6, 9, 16, 240));
        batch.FillRectangle(new RectangleF(barX, CardY + 61f, barW, 10f), new ColorRgba(18, 28, 48, 240));
        if (staRatio > 0.005f)
        {
            ColorRgba staCol = staRatio > 0.25f ? new ColorRgba(16, 185, 129, 255) : new ColorRgba(245, 158, 11, 255);
            batch.FillRectangle(new RectangleF(barX, CardY + 61f, barW * staRatio, 10f), staCol);
            batch.FillRectangle(new RectangleF(barX, CardY + 61f, barW * staRatio, 2f), new ColorRgba(255, 255, 255, 70));
        }

        // 4. XP bar strip (h=6)
        float xpRatio = Math.Clamp(world.LocalPlayerExperience / MathF.Max(1f, world.LocalPlayerXpToNext), 0f, 1f);
        batch.FillRectangle(new RectangleF(barX, CardY + 75f, barW, 5f), new ColorRgba(15, 23, 42, 240));
        if (xpRatio > 0.004f)
        {
            batch.FillRectangle(new RectangleF(barX, CardY + 75f, barW * xpRatio, 5f), new ColorRgba(168, 85, 247, 255));
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

        spriteBatch.DrawOutlinedString(font, $"{heroName} | DEF {(int)MathF.Round(world.LocalPlayerDefense)}", new Vector2(CardX + 68f, CardY + 8f), ColorRgba.White, black, 1, 1f);

        string lvText = $"LV.{world.LocalPlayerLevel}";
        TextMeasureResult lvSize = font.MeasureString(lvText, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, lvText, new Vector2(MathF.Floor(CardX + 8f + ((52f - lvSize.Width) * 0.5f)), CardY + 65f), new ColorRgba(250, 204, 21, 255), black, 1, 1f);

        string badgeText = world.TransportLabel;
        TextMeasureResult badgeSize = font.MeasureString(badgeText, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, badgeText, new Vector2(MathF.Floor(CardX + CardW - 78f + ((70f - badgeSize.Width) * 0.5f)), CardY + 9f), ColorRgba.White, black, 1, 1f);

        const float barX = CardX + 68f;
        const float barW = CardW - 76f;

        string hpText = $"HP {curHp}/{maxHp}";
        TextMeasureResult hpSize = font.MeasureString(hpText, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, hpText, new Vector2(MathF.Floor(barX + ((barW - hpSize.Width) * 0.5f)), CardY + 28f), ColorRgba.White, black, 1, 1f);

        string mpText = $"MP {(int)MathF.Floor(world.LocalPlayerMana)}/{(int)MathF.Ceiling(world.LocalPlayerMaxMana)}";
        TextMeasureResult mpSize = font.MeasureString(mpText, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, mpText, new Vector2(MathF.Floor(barX + ((barW - mpSize.Width) * 0.5f)), CardY + 46f), new ColorRgba(224, 242, 254, 255), black, 1, 1f);

        string staText = $"STA {(int)MathF.Floor(world.LocalPlayerStamina)}/{(int)MathF.Ceiling(world.LocalPlayerMaxStamina)}";
        TextMeasureResult staSize = font.MeasureString(staText, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, staText, new Vector2(MathF.Floor(barX + ((barW - staSize.Width) * 0.5f)), CardY + 62f), new ColorRgba(209, 250, 229, 255), black, 1, 1f);

        int h = (int)MathF.Floor(hourOfDay) % 24;
        int m = (int)MathF.Floor((hourOfDay - MathF.Floor(hourOfDay)) * 60f);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        ItemSlot activeSlot = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;
        bool wornOut = ToolUsagePolicy.IsWornOut(in activeSlot);
        string itemName = StarterGameCatalog.GetDisplayName(activeItemId);
        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in activeSlot);
        string upgTag = upg > 0 ? $"+{upg} " : string.Empty;
        string itemLabel = wornOut ? $"{upgTag}{itemName} [WORN]" : $"{upgTag}{itemName}";
        ColorRgba itemColor = wornOut ? new ColorRgba(248, 113, 113, 255) : new ColorRgba(226, 232, 240, 255);

        spriteBatch.DrawOutlinedString(font, $"{itemLabel} | XP {world.LocalPlayerExperience}/{world.LocalPlayerXpToNext}", new Vector2(CardX + 8f, CardY + 86f), itemColor, black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"{biome} ({ptx},{pty}) | {h:D2}:{m:D2} | {(int)fps} FPS", new Vector2(CardX + 8f, CardY + 101f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
    }
}

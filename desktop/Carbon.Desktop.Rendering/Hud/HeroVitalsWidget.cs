// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Biomes;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Top-left RPG hero unit frame skinned cleanly with <see cref="CarbonUiSkin"/>, showing a live
/// vector portrait, level &amp; unspent point badge, transport pill, HP/MP/Stamina/XP meters, and optional telemetry (SRP).
/// </summary>
internal sealed class HeroVitalsWidget
{
    private const float CardX = 16f;
    private const float CardY = 14f;
    private const float CardW = 348f;
    private const float CardH = 118f;

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        skin.DrawCard(spriteBatch, new RectangleF(CardX, CardY, CardW, CardH), new ColorRgba(250, 204, 21, 225), bodyAlpha: 234);
        skin.DrawSlot(spriteBatch, new RectangleF(CardX + 9f, CardY + 9f, 52f, 52f), new ColorRgba(12, 18, 34, 244), new ColorRgba(56, 189, 248, 225));
    }

    public void DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        CombatFeedbackTracker? feedback,
        float animTime)
    {
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        ItemSlot activeSlot = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;

        RectangleF portraitBox = new(CardX + 9f, CardY + 9f, 52f, 52f);
        CompiledItemVectorArt? equippedArt = null;
        if (!activeSlot.IsEmpty && art is not null)
        {
            _ = art.TryGetItem(activeSlot.ItemId, out equippedArt);
        }

        if (art is not null && art.TryGetCharacter(world.LocalPlayerAppearancePreset, out CompiledVectorRig? rig))
        {
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

        // Level badge below portrait
        RectangleF lvRect = new(portraitBox.X, portraitBox.Bottom + 4f, 52f, 15f);
        batch.FillRectangle(lvRect, new ColorRgba(24, 34, 54, 245));
        batch.DrawRectangle(lvRect, new ColorRgba(250, 204, 21, 220), 1f);

        if (equippedArt is not null)
        {
            bool wornOut = ToolUsagePolicy.IsWornOut(in activeSlot);
            RectangleF miniBox = new(portraitBox.Right - 17f, portraitBox.Bottom - 17f, 19f, 19f);
            batch.FillRectangle(miniBox, new ColorRgba(10, 15, 28, 245));
            VectorRigRenderer.DrawItemIcon(batch, equippedArt, new Vector2(miniBox.X + 9.5f, miniBox.Y + 9.5f), 13f);
            batch.DrawRectangle(miniBox, wornOut ? new ColorRgba(239, 68, 68, 255) : equippedArt.AccentColor, 1.2f);
        }

        // Transport status pill
        ColorRgba badgeColor = world.IsUdpActive
            ? new ColorRgba(16, 185, 129, 235)
            : (world.IsConnected ? new ColorRgba(37, 99, 235, 235) : new ColorRgba(185, 28, 28, 235));
        batch.FillRectangle(new RectangleF(CardX + CardW - 78f, CardY + 8f, 68f, 14f), badgeColor);

        const float barX = CardX + 70f;
        const float barW = CardW - 80f;

        // 1. HP bar (h=16)
        float maxHp = MathF.Max(1f, world.LocalPlayerMaxHealth);
        float ratio = Math.Clamp(world.LocalPlayerHealth / maxHp, 0f, 1f);
        float ghost = feedback is not null ? Math.Clamp(feedback.LocalGhostHpRatio, ratio, 1f) : ratio;
        batch.FillRectangle(new RectangleF(barX - 1f, CardY + 25f, barW + 2f, 18f), new ColorRgba(6, 9, 16, 245));
        batch.FillRectangle(new RectangleF(barX, CardY + 26f, barW, 16f), new ColorRgba(24, 34, 52, 240));
        if (ghost > ratio + 0.004f)
        {
            batch.FillRectangle(new RectangleF(barX, CardY + 26f, barW * ghost, 16f), new ColorRgba(253, 224, 71, 220));
        }

        float fillW = barW * ratio;
        if (fillW > 0.5f)
        {
            batch.FillRectangle(new RectangleF(barX, CardY + 26f, fillW, 16f), new ColorRgba(220, 38, 38, 255));
            batch.FillRectangle(new RectangleF(barX, CardY + 26f, fillW, 3.5f), new ColorRgba(255, 255, 255, 65));
        }

        // 2. MP bar (h=12)
        float mpRatio = Math.Clamp(world.LocalPlayerMana / MathF.Max(1f, world.LocalPlayerMaxMana), 0f, 1f);
        batch.FillRectangle(new RectangleF(barX - 1f, CardY + 45f, barW + 2f, 14f), new ColorRgba(6, 9, 16, 240));
        batch.FillRectangle(new RectangleF(barX, CardY + 46f, barW, 12f), new ColorRgba(18, 28, 48, 240));
        if (mpRatio > 0.005f)
        {
            batch.FillRectangle(new RectangleF(barX, CardY + 46f, barW * mpRatio, 12f), new ColorRgba(37, 99, 235, 255));
            batch.FillRectangle(new RectangleF(barX, CardY + 46f, barW * mpRatio, 2.5f), new ColorRgba(125, 211, 252, 90));
        }

        // 3. Stamina bar (h=10)
        float staRatio = Math.Clamp(world.LocalPlayerStamina / MathF.Max(1f, world.LocalPlayerMaxStamina), 0f, 1f);
        batch.FillRectangle(new RectangleF(barX - 1f, CardY + 61f, barW + 2f, 12f), new ColorRgba(6, 9, 16, 240));
        batch.FillRectangle(new RectangleF(barX, CardY + 62f, barW, 10f), new ColorRgba(18, 28, 48, 240));
        if (staRatio > 0.005f)
        {
            ColorRgba staCol = staRatio > 0.25f ? new ColorRgba(16, 185, 129, 255) : new ColorRgba(245, 158, 11, 255);
            batch.FillRectangle(new RectangleF(barX, CardY + 62f, barW * staRatio, 10f), staCol);
            batch.FillRectangle(new RectangleF(barX, CardY + 62f, barW * staRatio, 2f), new ColorRgba(255, 255, 255, 70));
        }

        // 4. XP bar strip (h=5)
        float xpRatio = Math.Clamp(world.LocalPlayerExperience / MathF.Max(1f, world.LocalPlayerXpToNext), 0f, 1f);
        batch.FillRectangle(new RectangleF(barX, CardY + 76f, barW, 5f), new ColorRgba(15, 23, 42, 240));
        if (xpRatio > 0.004f)
        {
            batch.FillRectangle(new RectangleF(barX, CardY + 76f, barW * xpRatio, 5f), new ColorRgba(168, 85, 247, 255));
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
        float fps,
        bool showTelemetry)
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

        spriteBatch.DrawOutlinedString(font, $"{heroName} | DEF {(int)MathF.Round(world.LocalPlayerDefense)}", new Vector2(CardX + 70f, CardY + 9f), ColorRgba.White, black, 1, 1f);

        RectangleF lvRect = new(CardX + 9f, CardY + 65f, 52f, 15f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"LV.{world.LocalPlayerLevel}", in lvRect, new ColorRgba(250, 204, 21, 255), black, 1f);

        RectangleF badgeRect = new(CardX + CardW - 78f, CardY + 8f, 68f, 14f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, world.TransportLabel, in badgeRect, ColorRgba.White, black, 1f);

        const float barX = CardX + 70f;
        const float barW = CardW - 80f;

        CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"HP {curHp}/{maxHp}", new RectangleF(barX, CardY + 26f, barW, 16f), ColorRgba.White, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"MP {(int)MathF.Floor(world.LocalPlayerMana)}/{(int)MathF.Ceiling(world.LocalPlayerMaxMana)}", new RectangleF(barX, CardY + 46f, barW, 12f), new ColorRgba(224, 242, 254, 255), black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"STA {(int)MathF.Floor(world.LocalPlayerStamina)}/{(int)MathF.Ceiling(world.LocalPlayerMaxStamina)}", new RectangleF(barX, CardY + 62f, barW, 10f), new ColorRgba(209, 250, 229, 255), black, 1f);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        ItemSlot activeSlot = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;
        bool wornOut = ToolUsagePolicy.IsWornOut(in activeSlot);
        string itemName = StarterGameCatalog.GetDisplayName(activeItemId);
        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in activeSlot);
        string upgTag = upg > 0 ? $"+{upg} " : string.Empty;
        string itemLabel = wornOut ? $"{upgTag}{itemName} [WORN]" : $"{upgTag}{itemName}";
        ColorRgba itemColor = wornOut ? new ColorRgba(248, 113, 113, 255) : new ColorRgba(226, 232, 240, 255);

        spriteBatch.DrawOutlinedString(font, $"{itemLabel} | XP {world.LocalPlayerExperience}/{world.LocalPlayerXpToNext}", new Vector2(CardX + 10f, CardY + 87f), itemColor, black, 1, 1f);

        if (world.LocalUnspentStatPoints > 0 || world.LocalUnspentSkillPoints > 0)
        {
            string ptsHint = world.LocalUnspentStatPoints > 0 && world.LocalUnspentSkillPoints > 0
                ? $"+{world.LocalUnspentStatPoints} AP [I] | +{world.LocalUnspentSkillPoints} SP [K]"
                : (world.LocalUnspentStatPoints > 0
                    ? $"+{world.LocalUnspentStatPoints} Attribute Pts [I]"
                    : $"+{world.LocalUnspentSkillPoints} Skill Pts [K]");
            spriteBatch.DrawOutlinedString(font, ptsHint, new Vector2(CardX + 10f, CardY + 101f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
        }
        else if (showTelemetry)
        {
            int h = (int)MathF.Floor(hourOfDay) % 24;
            int m = (int)MathF.Floor((hourOfDay - MathF.Floor(hourOfDay)) * 60f);
            spriteBatch.DrawOutlinedString(font, $"{biome} ({ptx},{pty}) | {h:D2}:{m:D2} | {(int)fps} FPS", new Vector2(CardX + 10f, CardY + 101f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Survival.Food;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Unified bottom action cluster: consolidated Level, HP, MP, Stamina, XP strip,
/// and centered 9-slot (<c>1..9</c>) hotbar framed cleanly with <see cref="CarbonUiPalette"/> (SRP).
/// </summary>
internal sealed class HotbarAndItemWidget
{
    private const int SlotCount = 9;
    private const float SlotSize = 54f;
    private const float Gap = 5f;
    private const float TotalWidth = (SlotCount * SlotSize) + ((SlotCount - 1) * Gap); // 526px
    private const int VitalsBarCount = 5; // HP, MP, stamina, hunger, rest
    private const byte TiredState = 1;
    private const byte VeryTiredState = 2;
    private const byte ExhaustedState = 3;
    private const byte HungryState = 1;
    private const byte UrgentHungerState = 2;
    private const byte StarvingState = 3;

    public void DrawSkin(
        SpriteBatch spriteBatch,
        CarbonUiSkin skin,
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight)
    {
        float startX = MathF.Floor((viewportWidth - TotalWidth) * 0.5f);
        float hotbarY = viewportHeight - SlotSize - 16f;

        skin.DrawCard(
            spriteBatch,
            new RectangleF(startX - 7f, hotbarY - 7f, TotalWidth + 14f, SlotSize + 14f),
            CarbonUiPalette.BorderOuter,
            bodyAlpha: 238);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int i = 0; i < SlotCount; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            bool active = i == world.SelectedHotbarSlot;
            float sy = active ? hotbarY - 3f : hotbarY;
            ItemSlot item = slots[i];
            ColorRgba border = active
                ? CarbonUiPalette.BorderActive
                : (!item.IsEmpty && ToolUsagePolicy.IsWornOut(world.Items.Rules, in item) ? CarbonUiPalette.DangerBorder : CarbonUiPalette.BorderSubtle);
            skin.DrawSlot(
                spriteBatch,
                new RectangleF(sx, sy, SlotSize, SlotSize),
                active ? CarbonUiPalette.SlotSelectedFill : CarbonUiPalette.SlotFill,
                border);
        }
    }

    public bool TryHitTestHotbar(Vector2 mousePos, int viewportWidth, int viewportHeight, out byte slotIndex)
    {
        slotIndex = 0;
        float startX = MathF.Floor((viewportWidth - TotalWidth) * 0.5f);
        float hotbarY = viewportHeight - SlotSize - 16f;

        if (mousePos.Y < hotbarY - 4f || mousePos.Y > hotbarY + SlotSize)
        {
            return false;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            if (mousePos.X >= sx && mousePos.X <= sx + SlotSize)
            {
                slotIndex = (byte)i;
                return true;
            }
        }

        return false;
    }

    public int DrawGeometry(
        GeometryBatch batch,
        IWorldReadModel world,
        IVectorArtRepository? art,
        CombatFeedbackTracker? feedback,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos,
        float animTime = 0f)
    {
        float startX = MathF.Floor((viewportWidth - TotalWidth) * 0.5f);
        float hotbarY = viewportHeight - SlotSize - 16f;
        float xpY = hotbarY - 15f;
        float barsY = xpY - 24f;

        DrawVitalsClusterGeometry(batch, world, feedback, startX, barsY, xpY);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        int hoveredSlot = -1;

        for (int i = 0; i < SlotCount; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            bool active = i == world.SelectedHotbarSlot;
            float sy = active ? hotbarY - 3f : hotbarY;
            if (mouseScreenPos.HasValue &&
                mouseScreenPos.Value.X >= sx && mouseScreenPos.Value.X <= sx + SlotSize &&
                mouseScreenPos.Value.Y >= sy && mouseScreenPos.Value.Y <= sy + SlotSize)
            {
                hoveredSlot = i;
            }

            ItemSlot item = slots[i];
            if (!item.IsEmpty)
            {
                RectangleF slotRect = new(sx, sy, SlotSize, SlotSize);
                byte tierLevel = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                EnhancementSlotDecor.DrawFill(batch, in slotRect, tierLevel, 3f);
                Vector2 iconCenter = new(sx + (SlotSize * 0.5f), sy + (SlotSize * 0.46f));
                if (art is not null && art.TryGetItem(item.ItemId, out CompiledItemVectorArt? itemArt))
                {
                    VectorRigRenderer.DrawItemIcon(batch, itemArt, iconCenter, targetSize: 34f);
                }

                EnhancementSlotDecor.DrawOverlay(batch, in slotRect, tierLevel, 0.8f, animTime);

                if (world.Items.Rules.Items.TryGet(item.ItemId, out ItemArchetype? archetype) && archetype.MaxDurability > 0f)
                {
                    byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                    float effMaxDur = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(archetype, upg));
                    float frac = Math.Clamp(item.Durability / effMaxDur, 0f, 1f);
                    ColorRgba durColor = frac <= 0f
                        ? CarbonUiPalette.HpFill
                        : (frac > 0.35f ? CarbonUiPalette.StaminaFill : CarbonUiPalette.XpFill);
                    batch.FillRectangle(new RectangleF(sx + 6f, sy + SlotSize - 7f, SlotSize - 12f, 3f), new ColorRgba(8, 10, 14, 240));
                    batch.FillRectangle(new RectangleF(sx + 6f, sy + SlotSize - 7f, MathF.Max(frac <= 0f ? SlotSize - 12f : 1f, (SlotSize - 12f) * frac), 3f), durColor);
                }
            }
        }

        if (hoveredSlot >= 0)
        {
            ItemSlot hItem = slots[hoveredSlot];
            if (!hItem.IsEmpty)
            {
                DrawTooltipGeometry(batch, world, art, startX + (hoveredSlot * (SlotSize + Gap)), barsY - 10f, viewportWidth, in hItem);
            }
        }

        return hoveredSlot;
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        int hoveredSlot)
    {
        float startX = MathF.Floor((viewportWidth - TotalWidth) * 0.5f);
        float hotbarY = viewportHeight - SlotSize - 16f;
        float xpY = hotbarY - 15f;
        float barsY = xpY - 24f;
        ColorRgba black = CarbonUiPalette.TextOutline;

        DrawVitalsClusterText(spriteBatch, font, world, startX, barsY);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        for (int i = 0; i < SlotCount; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            bool active = i == world.SelectedHotbarSlot;
            float sy = active ? hotbarY - 3f : hotbarY;
            spriteBatch.DrawOutlinedString(
                font,
                $"{i + 1}",
                new Vector2(sx + 4f, sy + 2f),
                active ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextMuted,
                black,
                1,
                1f);

            ItemSlot item = slots[i];
            RectangleF slotRect = new(sx, sy, SlotSize, SlotSize);
            EnhancementSlotDecor.DrawBadgeText(spriteBatch, font, in slotRect, ItemEnhancementPolicy.GetUpgradeLevel(in item), 1f, 0.8f);

            if (!item.IsEmpty && item.Quantity > 1)
            {
                string qty = $"{item.Quantity}";
                TextMeasureResult qSize = font.MeasureString(qty, TextLayoutOptions.Default, 1f);
                spriteBatch.DrawOutlinedString(font, qty, new Vector2(MathF.Floor(sx + SlotSize - qSize.Width - 4f), sy + SlotSize - 19f), CarbonUiPalette.TextPrimary, black, 1, 1f);
            }
        }

        if (hoveredSlot >= 0)
        {
            ItemSlot hItem = slots[hoveredSlot];
            if (!hItem.IsEmpty)
            {
                DrawTooltipText(spriteBatch, font, world, art, startX + (hoveredSlot * (SlotSize + Gap)), barsY - 10f, viewportWidth, in hItem);
            }
        }
    }

    private static void DrawVitalsClusterGeometry(
        GeometryBatch batch,
        IWorldReadModel world,
        CombatFeedbackTracker? feedback,
        float startX,
        float barsY,
        float xpY)
    {
        const float lvW = 58f;
        const float segGap = 6f;
        const float barW = (TotalWidth - lvW - (VitalsBarCount * segGap)) / VitalsBarCount; // ~88px
        const float barH = 20f;

        // Level badge
        RectangleF lvRect = new(startX, barsY, lvW, barH);
        batch.FillRectangle(lvRect, CarbonUiPalette.WindowFill);
        batch.DrawRectangle(lvRect, world.LocalUnspentStatPoints > 0 ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderOuter, 1.2f);

        // HP bar
        float hpX = startX + lvW + segGap;
        float maxHp = MathF.Max(1f, world.LocalPlayerMaxHealth);
        float hpRatio = Math.Clamp(world.LocalPlayerHealth / maxHp, 0f, 1f);
        float ghostRatio = feedback is not null ? Math.Clamp(feedback.LocalGhostHpRatio, hpRatio, 1f) : hpRatio;
        DrawMeterGeometry(batch, new RectangleF(hpX, barsY, barW, barH), hpRatio, ghostRatio, CarbonUiPalette.HpFill);

        // MP bar
        float mpX = hpX + barW + segGap;
        float mpRatio = Math.Clamp(world.LocalPlayerMana / MathF.Max(1f, world.LocalPlayerMaxMana), 0f, 1f);
        DrawMeterGeometry(batch, new RectangleF(mpX, barsY, barW, barH), mpRatio, mpRatio, CarbonUiPalette.MpFill);

        // Stamina bar
        float staX = mpX + barW + segGap;
        float staRatio = Math.Clamp(world.LocalPlayerStamina / MathF.Max(1f, world.LocalPlayerMaxStamina), 0f, 1f);
        DrawMeterGeometry(batch, new RectangleF(staX, barsY, barW, barH), staRatio, staRatio, CarbonUiPalette.StaminaFill);

        // Hunger bar (food meter) with a thin meal-progress strip while eating
        float foodX = staX + barW + segGap;
        float foodRatio = Math.Clamp(world.LocalPlayerFood / MathF.Max(1f, world.LocalPlayerMaxFood), 0f, 1f);
        ColorRgba foodFill = world.LocalPlayerFoodPoisoned ? CarbonUiPalette.PoisonFill
            : world.LocalPlayerHungerState >= StarvingState ? CarbonUiPalette.HpFill
            : world.LocalPlayerHungerState >= HungryState ? CarbonUiPalette.HungerWarnFill
            : CarbonUiPalette.HungerFill;
        DrawMeterGeometry(batch, new RectangleF(foodX, barsY, barW, barH), foodRatio, foodRatio, foodFill);
        float eatProgress = world.LocalPlayerEatProgress;
        if (eatProgress >= 0f)
        {
            batch.FillRectangle(
                new RectangleF(foodX, barsY + barH - 3f, barW * Math.Clamp(eatProgress, 0f, 1f), 3f),
                new ColorRgba(255, 255, 255, 200));
        }

        // Rest bar (ADR-018); a soft top highlight while sleeping or sitting
        float restX = foodX + barW + segGap;
        float restRatio = Math.Clamp(world.LocalPlayerRest / MathF.Max(1f, world.LocalPlayerMaxRest), 0f, 1f);
        ColorRgba restFill = world.LocalPlayerRestState >= VeryTiredState ? CarbonUiPalette.RestWarnFill : CarbonUiPalette.RestFill;
        DrawMeterGeometry(batch, new RectangleF(restX, barsY, barW, barH), restRatio, restRatio, restFill);
        if (world.LocalPlayerRestMode != RestMode.None)
        {
            batch.FillRectangle(new RectangleF(restX, barsY, barW, 3f), new ColorRgba(200, 220, 255, 170));
        }

        // Thin XP bar across the hotbar width
        float xpRatio = Math.Clamp(world.LocalPlayerExperience / MathF.Max(1f, world.LocalPlayerXpToNext), 0f, 1f);
        RectangleF xpTrack = new(startX, xpY, TotalWidth, 5f);
        batch.FillRectangle(xpTrack, CarbonUiPalette.SlotFill);
        if (xpRatio > 0.004f)
        {
            batch.FillRectangle(new RectangleF(startX, xpY, TotalWidth * xpRatio, 5f), CarbonUiPalette.XpFill);
        }

        batch.DrawRectangle(xpTrack, CarbonUiPalette.BorderSubtle, 1f);
    }

    private static void DrawMeterGeometry(
        GeometryBatch batch,
        in RectangleF rect,
        float ratio,
        float ghostRatio,
        ColorRgba fillColor)
    {
        batch.FillRectangle(new RectangleF(rect.X - 1f, rect.Y - 1f, rect.Width + 2f, rect.Height + 2f), new ColorRgba(8, 10, 14, 245));
        batch.FillRectangle(rect, CarbonUiPalette.SlotFill);
        if (ghostRatio > ratio + 0.004f)
        {
            batch.FillRectangle(new RectangleF(rect.X, rect.Y, rect.Width * ghostRatio, rect.Height), CarbonUiPalette.BorderActive);
        }

        float fillW = rect.Width * ratio;
        if (fillW > 0.5f)
        {
            batch.FillRectangle(new RectangleF(rect.X, rect.Y, fillW, rect.Height), fillColor);
            batch.FillRectangle(new RectangleF(rect.X, rect.Y, fillW, 3f), new ColorRgba(255, 255, 255, 50));
        }

        batch.DrawRectangle(rect, CarbonUiPalette.BorderSubtle, 1f);
    }

    private static void DrawVitalsClusterText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        float startX,
        float barsY)
    {
        const float lvW = 58f;
        const float segGap = 6f;
        const float barW = (TotalWidth - lvW - (VitalsBarCount * segGap)) / VitalsBarCount;
        const float barH = 20f;
        ColorRgba black = CarbonUiPalette.TextOutline;

        string lvText = world.LocalUnspentStatPoints > 0
            ? $"Lv.{world.LocalPlayerLevel}+"
            : $"Lv.{world.LocalPlayerLevel}";
        CarbonUiSkin.DrawCenteredText(
            spriteBatch,
            font,
            lvText,
            new RectangleF(startX, barsY, lvW, barH),
            world.LocalUnspentStatPoints > 0 ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextPrimary,
            black,
            1f);

        float hpX = startX + lvW + segGap;
        string hpText = $"{(int)MathF.Ceiling(world.LocalPlayerHealth)}/{(int)MathF.Ceiling(world.LocalPlayerMaxHealth)}";
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, hpText, new RectangleF(hpX, barsY, barW, barH), CarbonUiPalette.TextPrimary, black, 1f);

        float mpX = hpX + barW + segGap;
        string mpText = $"{(int)MathF.Floor(world.LocalPlayerMana)}/{(int)MathF.Ceiling(world.LocalPlayerMaxMana)}";
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, mpText, new RectangleF(mpX, barsY, barW, barH), CarbonUiPalette.TextPrimary, black, 1f);

        float staX = mpX + barW + segGap;
        string staText = $"{(int)MathF.Floor(world.LocalPlayerStamina)}/{(int)MathF.Ceiling(world.LocalPlayerMaxStamina)}";
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, staText, new RectangleF(staX, barsY, barW, barH), CarbonUiPalette.TextPrimary, black, 1f);

        float foodX = staX + barW + segGap;
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, BuildHungerLabel(world), new RectangleF(foodX, barsY, barW, barH), CarbonUiPalette.TextPrimary, black, 1f);

        float restX = foodX + barW + segGap;
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, BuildRestLabel(world), new RectangleF(restX, barsY, barW, barH), CarbonUiPalette.TextPrimary, black, 1f);
    }

    private static string BuildRestLabel(IWorldReadModel world)
    {
        int pct = (int)MathF.Round(100f * Math.Clamp(world.LocalPlayerRest / MathF.Max(1f, world.LocalPlayerMaxRest), 0f, 1f));
        return world.LocalPlayerRestMode switch
        {
            RestMode.Sitting => $"Sit {pct}%",
            RestMode.Sleeping => $"Zzz {pct}%",
            RestMode.SleepingComfortable => $"Zzz+ {pct}%",
            RestMode.None => world.LocalPlayerRestState switch
            {
                ExhaustedState => "Exhausted",
                VeryTiredState => $"Drowsy {pct}%",
                TiredState => $"Tired {pct}%",
                _ => $"Rest {pct}%",
            },
            _ => $"Rest {pct}%",
        };
    }

    private static string BuildHungerLabel(IWorldReadModel world)
    {
        if (world.LocalPlayerEatProgress >= 0f)
        {
            return "Eating...";
        }

        int pct = (int)MathF.Round(100f * Math.Clamp(world.LocalPlayerFood / MathF.Max(1f, world.LocalPlayerMaxFood), 0f, 1f));
        return world.LocalPlayerHungerState switch
        {
            HungryState => $"Hungry {pct}%",
            UrgentHungerState => "Ravenous",
            StarvingState => "Starving!",
            _ => world.LocalPlayerFoodPoisoned ? $"Sick {pct}%" : $"Food {pct}%",
        };
    }

    private static void DrawTooltipGeometry(
        GeometryBatch batch,
        IWorldReadModel world,
        IVectorArtRepository? art,
        float slotX,
        float topY,
        int viewportWidth,
        in ItemSlot item)
    {
        const float cardW = 264f;
        const float cardH = 66f;
        float x = Math.Clamp(MathF.Floor(slotX + (SlotSize * 0.5f) - (cardW * 0.5f)), 12f, viewportWidth - cardW - 12f);
        float y = topY - cardH;

        batch.FillRectangle(new RectangleF(x, y, cardW, cardH), CarbonUiPalette.WindowFill);
        batch.DrawRectangle(new RectangleF(x, y, cardW, cardH), CarbonUiPalette.BorderOuter, 1.4f);

        RectangleF iconBox = new(x + 8f, y + 8f, 38f, 38f);
        batch.FillRectangle(iconBox, CarbonUiPalette.SlotFill);
        if (art is not null && art.TryGetItem(item.ItemId, out CompiledItemVectorArt? bp))
        {
            VectorRigRenderer.DrawItemIcon(batch, bp, new Vector2(x + 27f, y + 27f), 26f);
        }

        batch.DrawRectangle(iconBox, CarbonUiPalette.BorderSubtle, 1f);

        if (world.Items.Rules.Items.TryGet(item.ItemId, out ItemArchetype? archetype) && archetype.MaxDurability > 0f)
        {
            byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
            float effMaxDur = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(archetype, upg));
            float frac = Math.Clamp(item.Durability / effMaxDur, 0f, 1f);
            ColorRgba durFill = frac <= 0f
                ? CarbonUiPalette.HpFill
                : (frac > 0.35f ? CarbonUiPalette.StaminaFill : CarbonUiPalette.XpFill);
            RectangleF durBg = new(x + 8f, y + 53f, cardW - 16f, 5f);
            batch.FillRectangle(durBg, CarbonUiPalette.SlotFill);
            batch.FillRectangle(new RectangleF(x + 8f, y + 53f, (cardW - 16f) * frac, 5f), durFill);
        }
    }

    private static void DrawTooltipText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        IVectorArtRepository? art,
        float slotX,
        float topY,
        int viewportWidth,
        in ItemSlot item)
    {
        const float cardW = 264f;
        const float cardH = 66f;
        float x = Math.Clamp(MathF.Floor(slotX + (SlotSize * 0.5f) - (cardW * 0.5f)), 12f, viewportWidth - cardW - 12f);
        float y = topY - cardH;
        ColorRgba black = CarbonUiPalette.TextOutline;

        string name = world.Items.GetDisplayName(item.ItemId);
        if (art is not null && art.TryGetItem(item.ItemId, out CompiledItemVectorArt? bp))
        {
            name = bp.DisplayName;
        }

        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
        string titleName = upg > 0 ? $"{name} +{upg}" : name;
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, titleName, cardW - 62f, 1f), new Vector2(x + 54f, y + 8f), CarbonUiPalette.TextWarm, black, 1, 1f);

        float upgMult = ItemEnhancementPolicy.GetStatMultiplier(upg);
        string stats = world.Items.Rules.Items.TryGet(item.ItemId, out ItemArchetype? def)
            ? def.Category switch
            {
                ItemCategory.Armor => $"+{(int)MathF.Round(def.ArmorDefense * upgMult)} DEF  +{(int)MathF.Round(def.BonusHealth * upgMult)} HP",
                ItemCategory.Weapon => $"+{(int)MathF.Round(def.BaseDamage * upgMult)} DMG  RNG {(int)def.AttackRange}",
                ItemCategory.Tool => $"Tier {def.HarvestTier} Tool",
                ItemCategory.Consumable => $"+{(int)def.HealHealth} HP  +{(int)def.RestoreMana} MP",
                ItemCategory.Placeable or ItemCategory.Accessory or ItemCategory.Resource or ItemCategory.Quest or ItemCategory.None or _ => $"Stack {item.Quantity}/{def.MaxStackSize}",
            }
            : $"x{item.Quantity}";

        if (SyncedFoodCatalog.TryGet(world.Items, item.ItemId, out FoodDefinition? food))
        {
            stats = SyncedFoodCatalog.FormatTooltip(food, item.FreshnessSeconds);
        }

        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, stats, cardW - 62f, 1f), new Vector2(x + 54f, y + 29f), CarbonUiPalette.TextMuted, black, 1, 1f);
    }
}

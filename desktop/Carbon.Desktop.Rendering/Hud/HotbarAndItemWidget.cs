// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
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

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Quick-access dock action next to the bottom hotbar.</summary>
public enum HudDockAction : byte
{
    /// <summary>No dock button hit.</summary>
    None = 0,

    /// <summary>Toggles the hero equipment and bag modal (<c>[I]</c>).</summary>
    Inventory = 1,

    /// <summary>Toggles the Arcane &amp; Martial Skill Tome (<c>[K]</c>).</summary>
    Skills = 2,

    /// <summary>Toggles the crafting workshop modal (<c>[C]</c>).</summary>
    Crafting = 3,

    /// <summary>Toggles the blacksmith forge anvil modal (<c>[U]</c>).</summary>
    Forge = 4,

    /// <summary>Opens the Settings &amp; Controls guide modal (<c>[ESC]</c>).</summary>
    Settings = 5,
}

/// <summary>
/// Renders the unified bottom 9-slot hotbar skinned with <see cref="CarbonUiSkin"/>, crisp <c>.cvg.json</c> vector item art,
/// color-graded durability bars, quick-access dock (<c>BAG / SKL / CFG / CRAFT / FORGE</c>), optional hint bar, and tooltips (SRP).
/// </summary>
internal sealed class HotbarAndItemWidget
{
    private const int SlotCount = 9;
    private const float SlotSize = 44f;
    private const float Gap = 4f;

    public bool TryHitTestDockButton(Vector2 mousePos, int viewportWidth, int viewportHeight, bool showHints, out HudDockAction action)
    {
        (RectangleF bag, RectangleF skills, RectangleF settings, RectangleF craft, RectangleF forge) =
            GetDockRects(viewportWidth, viewportHeight, showHints);
        if (bag.Contains(mousePos))
        {
            action = HudDockAction.Inventory;
            return true;
        }

        if (skills.Contains(mousePos))
        {
            action = HudDockAction.Skills;
            return true;
        }

        if (settings.Contains(mousePos))
        {
            action = HudDockAction.Settings;
            return true;
        }

        if (craft.Contains(mousePos))
        {
            action = HudDockAction.Crafting;
            return true;
        }

        if (forge.Contains(mousePos))
        {
            action = HudDockAction.Forge;
            return true;
        }

        action = HudDockAction.None;
        return false;
    }

    public void DrawSkin(
        SpriteBatch spriteBatch,
        CarbonUiSkin skin,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        bool showBag,
        bool showSkills,
        bool showCraft,
        bool showForge,
        bool showSettings,
        bool showHints)
    {
        const float totalW = (SlotCount * SlotSize) + ((SlotCount - 1) * Gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - (showHints ? 75f : 56f);

        skin.DrawCard(spriteBatch, new RectangleF(startX - 8f, startY - 8f, totalW + 16f, SlotSize + 16f), new ColorRgba(71, 85, 105, 225), bodyAlpha: 230);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int i = 0; i < SlotCount; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            bool active = i == world.SelectedHotbarSlot;
            float sy = active ? startY - 3f : startY;
            ItemSlot item = slots[i];
            ColorRgba border = active
                ? new ColorRgba(250, 204, 21, 255)
                : (!item.IsEmpty && ToolUsagePolicy.IsWornOut(in item) ? new ColorRgba(239, 68, 68, 255) : new ColorRgba(71, 85, 105, 220));
            skin.DrawSlot(spriteBatch, new RectangleF(sx, sy, SlotSize, SlotSize), active ? new ColorRgba(26, 38, 62, 245) : new ColorRgba(12, 18, 34, 236), border);
        }

        (RectangleF bagRect, RectangleF skillsRect, RectangleF cfgRect, RectangleF craftRect, RectangleF forgeRect) =
            GetDockRects(viewportWidth, viewportHeight, showHints);

        bool hasAp = world.LocalUnspentStatPoints > 0;
        bool hasSp = world.LocalUnspentSkillPoints > 0;
        skin.DrawButton(
            spriteBatch,
            in bagRect,
            showBag ? new ColorRgba(180, 83, 9, 240) : new ColorRgba(16, 24, 42, 232),
            showBag || hasAp ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(100, 116, 139, 220));
        skin.DrawButton(
            spriteBatch,
            in skillsRect,
            showSkills ? new ColorRgba(14, 116, 144, 240) : new ColorRgba(16, 24, 42, 232),
            showSkills || hasSp ? new ColorRgba(56, 189, 248, 255) : new ColorRgba(100, 116, 139, 220));
        skin.DrawButton(
            spriteBatch,
            in cfgRect,
            showSettings ? new ColorRgba(51, 65, 85, 240) : new ColorRgba(16, 24, 42, 232),
            showSettings ? ColorRgba.White : new ColorRgba(100, 116, 139, 220));
        skin.DrawButton(
            spriteBatch,
            in craftRect,
            showCraft ? new ColorRgba(180, 83, 9, 240) : new ColorRgba(16, 24, 42, 232),
            showCraft ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(100, 116, 139, 220));

        bool nearStation = world.IsNearForgeStation();
        skin.DrawButton(
            spriteBatch,
            in forgeRect,
            showForge ? new ColorRgba(194, 65, 12, 240) : (nearStation ? new ColorRgba(154, 52, 18, 234) : new ColorRgba(16, 24, 42, 215)),
            showForge || nearStation ? new ColorRgba(251, 146, 60, 255) : new ColorRgba(71, 85, 105, 200));
    }

    public bool TryHitTestHotbar(Vector2 mousePos, int viewportWidth, int viewportHeight, bool showHints, out byte slotIndex)
    {
        slotIndex = 0;
        const float totalW = (SlotCount * SlotSize) + ((SlotCount - 1) * Gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - (showHints ? 75f : 56f);

        if (mousePos.Y < startY - 5f || mousePos.Y > startY + SlotSize)
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
        BitmapFont font,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        bool showHints,
        Vector2? mouseScreenPos)
    {
        const float totalW = (SlotCount * SlotSize) + ((SlotCount - 1) * Gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - (showHints ? 75f : 56f);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        int hoveredSlot = -1;

        for (int i = 0; i < SlotCount; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            bool active = i == world.SelectedHotbarSlot;
            float sy = active ? startY - 3f : startY;
            if (mouseScreenPos.HasValue &&
                mouseScreenPos.Value.X >= sx && mouseScreenPos.Value.X <= sx + SlotSize &&
                mouseScreenPos.Value.Y >= sy && mouseScreenPos.Value.Y <= sy + SlotSize)
            {
                hoveredSlot = i;
            }

            ItemSlot item = slots[i];
            if (!item.IsEmpty)
            {
                Vector2 iconCenter = new(sx + (SlotSize * 0.5f), sy + (SlotSize * 0.46f));
                if (art is not null && art.TryGetItem(item.ItemId, out CompiledItemVectorArt? itemArt))
                {
                    VectorRigRenderer.DrawItemIcon(batch, itemArt, iconCenter, targetSize: 28f);
                }

                if (StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? archetype) && archetype.MaxDurability > 0f)
                {
                    byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                    float effMaxDur = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(archetype, upg));
                    float frac = Math.Clamp(item.Durability / effMaxDur, 0f, 1f);
                    ColorRgba durColor = frac <= 0f
                        ? new ColorRgba(239, 68, 68, 255)
                        : (frac > 0.5f ? new ColorRgba(56, 189, 248, 240) : (frac > 0.2f ? new ColorRgba(250, 204, 21, 240) : new ColorRgba(239, 68, 68, 245)));
                    batch.FillRectangle(new RectangleF(sx + 5f, sy + SlotSize - 7f, SlotSize - 10f, 3f), new ColorRgba(15, 23, 42, 235));
                    batch.FillRectangle(new RectangleF(sx + 5f, sy + SlotSize - 7f, MathF.Max(frac <= 0f ? SlotSize - 10f : 1f, (SlotSize - 10f) * frac), 3f), durColor);
                }
            }
        }

        if (hoveredSlot >= 0)
        {
            ItemSlot hItem = slots[hoveredSlot];
            if (!hItem.IsEmpty)
            {
                DrawTooltipGeometry(batch, art, startX + (hoveredSlot * (SlotSize + Gap)), startY - 12f, viewportWidth, in hItem);
            }
        }

        if (showHints)
        {
            string hint = ResolveHintText(out float hintScale);
            TextMeasureResult measure = font.MeasureString(hint, TextLayoutOptions.Default, hintScale);
            float hintX = MathF.Floor((viewportWidth - measure.Width) * 0.5f);
            batch.FillRectangle(new RectangleF(hintX - 8f, viewportHeight - 23f, measure.Width + 16f, 18f), new ColorRgba(10, 15, 28, 200));
        }

        return hoveredSlot;
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        bool showHints,
        int hoveredSlot)
    {
        const float totalW = (SlotCount * SlotSize) + ((SlotCount - 1) * Gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - (showHints ? 75f : 56f);
        ColorRgba black = new(10, 12, 18, 255);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        for (int i = 0; i < SlotCount; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            float sy = i == world.SelectedHotbarSlot ? startY - 3f : startY;
            spriteBatch.DrawOutlinedString(font, $"{i + 1}", new Vector2(sx + 4f, sy + 4f), new ColorRgba(203, 213, 225, 255), black, 1, 1f);
            ItemSlot item = slots[i];
            byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
            if (upg > 0)
            {
                string plusTxt = $"+{upg}";
                ColorRgba plusCol = upg >= 6 ? new ColorRgba(34, 211, 238, 255) : new ColorRgba(250, 204, 21, 255);
                spriteBatch.DrawOutlinedString(font, plusTxt, new Vector2(sx + SlotSize - 17f, sy + 4f), plusCol, black, 1, 1f);
            }

            if (!item.IsEmpty && item.Quantity > 1)
            {
                string qty = $"x{item.Quantity}";
                TextMeasureResult qSize = font.MeasureString(qty, TextLayoutOptions.Default, 1.5f);
                spriteBatch.DrawOutlinedString(font, qty, new Vector2(MathF.Floor(sx + SlotSize - qSize.Width - 4f), sy + SlotSize - 17f), ColorRgba.White, black, 1, 1.5f);
            }
        }

        if (hoveredSlot >= 0)
        {
            ItemSlot hItem = slots[hoveredSlot];
            if (!hItem.IsEmpty)
            {
                DrawTooltipText(spriteBatch, font, art, startX + (hoveredSlot * (SlotSize + Gap)), startY - 12f, viewportWidth, in hItem);
            }
        }

        if (showHints)
        {
            string hint = ResolveHintText(out float hintScale);
            TextMeasureResult hSize = font.MeasureString(hint, TextLayoutOptions.Default, hintScale);
            float hintX = MathF.Floor((viewportWidth - hSize.Width) * 0.5f);
            spriteBatch.DrawOutlinedString(font, hint, new Vector2(hintX, viewportHeight - 20f), new ColorRgba(226, 232, 240, 255), black, 1, hintScale);
        }

        (RectangleF bagRect, RectangleF skillsRect, RectangleF cfgRect, RectangleF craftRect, RectangleF forgeRect) =
            GetDockRects(viewportWidth, viewportHeight, showHints);

        string bagLabel = world.LocalUnspentStatPoints > 0 ? $"BAG+{world.LocalUnspentStatPoints}" : "[I] BAG";
        string skLabel = world.LocalUnspentSkillPoints > 0 ? $"SKL+{world.LocalUnspentSkillPoints}" : "[K] SKL";
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, bagLabel, in bagRect, world.LocalUnspentStatPoints > 0 ? new ColorRgba(253, 224, 71, 255) : ColorRgba.White, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, skLabel, in skillsRect, world.LocalUnspentSkillPoints > 0 ? new ColorRgba(125, 211, 252, 255) : ColorRgba.White, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "CFG", in cfgRect, new ColorRgba(226, 232, 240, 255), black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "[C] CRAFT", in craftRect, ColorRgba.White, black, 1f);
        ColorRgba forgeTxtCol = world.IsNearForgeStation() ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(148, 163, 184, 255);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "[U] FORGE", in forgeRect, forgeTxtCol, black, 1f);
    }

    private static void DrawTooltipGeometry(
        GeometryBatch batch,
        IVectorArtRepository? art,
        float slotX,
        float hotbarTopY,
        int viewportWidth,
        in ItemSlot item)
    {
        const float cardW = 336f;
        const float cardH = 108f;
        float x = Math.Clamp(MathF.Floor(slotX + (SlotSize * 0.5f) - (cardW * 0.5f)), 12f, viewportWidth - cardW - 12f);
        float y = hotbarTopY - cardH;

        ColorRgba accent = new(148, 163, 184, 255);
        CompiledItemVectorArt? bp = null;
        if (art is not null && art.TryGetItem(item.ItemId, out bp))
        {
            accent = bp.AccentColor;
        }

        batch.FillRectangle(new RectangleF(x, y, cardW, cardH), new ColorRgba(8, 12, 24, 245));
        batch.DrawRectangle(new RectangleF(x, y, cardW, cardH), accent, 1.5f);
        batch.FillRectangle(new RectangleF(x + 2f, y + 2f, cardW - 4f, 2f), new ColorRgba(accent.R, accent.G, accent.B, 90));

        RectangleF iconBox = new(x + 8f, y + 8f, 42f, 42f);
        batch.FillRectangle(iconBox, new ColorRgba(15, 23, 42, 245));
        if (bp is not null)
        {
            VectorRigRenderer.DrawItemIcon(batch, bp, new Vector2(x + 29f, y + 29f), 30f);
        }

        batch.DrawRectangle(iconBox, accent, 1f);

        if (StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? archetype) && archetype.MaxDurability > 0f)
        {
            byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
            float effMaxDur = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(archetype, upg));
            float frac = Math.Clamp(item.Durability / effMaxDur, 0f, 1f);
            ColorRgba durFill = frac <= 0f
                ? new ColorRgba(239, 68, 68, 255)
                : (frac > 0.5f ? new ColorRgba(56, 189, 248, 245) : (frac > 0.2f ? new ColorRgba(250, 204, 21, 245) : new ColorRgba(239, 68, 68, 245)));
            RectangleF durBg = new(x + 10f, y + 92f, cardW - 20f, 8f);
            batch.FillRectangle(durBg, new ColorRgba(15, 23, 42, 245));
            batch.FillRectangle(new RectangleF(x + 10f, y + 92f, (cardW - 20f) * frac, 8f), durFill);
            batch.DrawRectangle(durBg, new ColorRgba(51, 65, 85, 220), 1f);
        }
    }

    private static void DrawTooltipText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IVectorArtRepository? art,
        float slotX,
        float hotbarTopY,
        int viewportWidth,
        in ItemSlot item)
    {
        const float cardW = 336f;
        const float cardH = 108f;
        float x = Math.Clamp(MathF.Floor(slotX + (SlotSize * 0.5f) - (cardW * 0.5f)), 12f, viewportWidth - cardW - 12f);
        float y = hotbarTopY - cardH;
        ColorRgba black = new(10, 12, 18, 255);

        string name = StarterGameCatalog.GetDisplayName(item.ItemId);
        byte tier = 1;
        string cat = "Item";
        ColorRgba titleColor = ColorRgba.White;
        if (art is not null && art.TryGetItem(item.ItemId, out CompiledItemVectorArt? bp))
        {
            name = bp.DisplayName;
            tier = bp.Tier;
            cat = bp.CategoryLabel;
            titleColor = bp.AccentColor;
        }

        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
        string titleName = upg > 0 ? $"{name} +{upg}" : name;
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, titleName, cardW - 68f, 1.5f), new Vector2(x + 58f, y + 10f), titleColor, black, 1, 1.5f);
        spriteBatch.DrawOutlinedString(font, $"Tier {tier} | {cat}", new Vector2(x + 58f, y + 29f), new ColorRgba(203, 213, 225, 255), black, 1, 1f);

        float upgMult = ItemEnhancementPolicy.GetStatMultiplier(upg);
        string stats;
        if (StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? def))
        {
            stats = def.Category switch
            {
                ItemCategory.Armor => $"DEF +{(int)MathF.Round(def.ArmorDefense * upgMult)} | +{(int)MathF.Round(def.BonusHealth * upgMult)} HP | +{(int)MathF.Round(def.BonusMana * upgMult)} MP",
                ItemCategory.Weapon or ItemCategory.Tool => $"DMG {(int)MathF.Round(def.BaseDamage * upgMult)} | Chop {(int)MathF.Round(def.FloraHarvestDamage * upgMult)} | Mine {(int)MathF.Round(def.MineralHarvestDamage * upgMult)}",
                ItemCategory.Consumable => $"Restore +{(int)def.HealHealth} HP | +{(int)def.RestoreMana} MP | +{(int)def.RestoreStamina} STA",
                ItemCategory.Placeable => $"Buildable Structure | Stack x{item.Quantity}/{def.MaxStackSize}",
                ItemCategory.Accessory or ItemCategory.Resource or ItemCategory.Quest or ItemCategory.None or _ => $"Stack x{item.Quantity}/{def.MaxStackSize} | Craft/Forge Material",
            };
        }
        else
        {
            stats = $"Stack x{item.Quantity}";
        }

        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, stats, cardW - 20f, 1f), new Vector2(x + 10f, y + 56f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, StarterGameCatalog.GetActionHint(item.ItemId), cardW - 20f, 1f), new Vector2(x + 10f, y + 71f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);

        if (StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? archetype) && archetype.MaxDurability > 0f)
        {
            float effMaxDur = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(archetype, upg));
            string durLabel = item.Durability <= 0f
                ? $"DURABILITY 0/{(int)effMaxDur} [WORN OUT]"
                : $"DURABILITY {(int)MathF.Ceiling(item.Durability)}/{(int)effMaxDur}";
            ColorRgba durTxtColor = item.Durability <= 0f ? new ColorRgba(252, 165, 165, 255) : ColorRgba.White;
            spriteBatch.DrawOutlinedString(font, durLabel, new Vector2(x + 14f, y + 90f), durTxtColor, black, 1, 1f);
        }
    }

    private static (RectangleF Bag, RectangleF Skills, RectangleF Settings, RectangleF Craft, RectangleF Forge) GetDockRects(
        int viewportWidth,
        int viewportHeight,
        bool showHints)
    {
        const float totalW = (SlotCount * SlotSize) + ((SlotCount - 1) * Gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - (showHints ? 75f : 56f);
        float dockX = MathF.Min(viewportWidth - 206f, startX + totalW + 12f);

        return (
            new RectangleF(dockX, startY - 1f, 64f, 22f),
            new RectangleF(dockX + 67f, startY - 1f, 64f, 22f),
            new RectangleF(dockX + 134f, startY - 1f, 58f, 22f),
            new RectangleF(dockX, startY + 24f, 94f, 22f),
            new RectangleF(dockX + 98f, startY + 24f, 94f, 22f));
    }

    private static string ResolveHintText(out float scale)
    {
        scale = 1f;
        return "[WASD] Move [TAB] Lock [Q/R/V] Skill [E] Loot/Smith [I] Bag [K] Skills [C] Craft [U] Forge [M] Map [ESC] Menu";
    }
}

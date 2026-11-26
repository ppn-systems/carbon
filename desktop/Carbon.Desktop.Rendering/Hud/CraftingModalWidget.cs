// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
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
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Two-pane Crafting Workshop tab (<c>[C]</c>) framed cleanly with <see cref="CarbonUiPalette"/>
/// and shared <see cref="PlayerModalTabs"/> (SRP).
/// </summary>
internal sealed class CraftingModalWidget
{
    private const float PanelW = 744f;
    private const float PanelH = 468f;
    private const int MaxVisibleCards = 6;
    private const float CardRowH = 54f;

    private static readonly string[] s_tabLabels = ["ALL", "WEAPONS", "ARMOR", "TOOLS", "BUILD", "POTIONS"];

    private readonly List<SkillDefinition> _skillScratch = new(8);
    private int _activeTab;
    private int _scrollOffset;
    private ushort _selectedRecipeId = 1;
    private bool _craftableOnly;

    public bool ContainsPoint(IClientWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        if (mousePos.X >= ox && mousePos.X <= ox + PanelW && mousePos.Y >= oy && mousePos.Y <= oy + PanelH)
        {
            return true;
        }

        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        return PlayerModalTabs.GetTabBarBounds(ox, oy, hasSkills).Contains(mousePos);
    }

    public bool TryHitTestCloseButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return PlayerModalTabs.GetCloseButtonRect(ox, PanelW, oy).Contains(mousePos);
    }

    public bool TryHitTestTab(IClientWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight, out PlayerModalTab tab)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        return PlayerModalTabs.TryHitTestTab(mousePos, ox, oy, hasSkills, out tab);
    }

    public void Scroll(int delta) => _scrollOffset = Math.Max(0, _scrollOffset + delta);

    public bool TryHandleClick(
        Vector2 mousePos,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        out ushort craftRecipeId,
        out ushort craftQuantity)
    {
        craftRecipeId = 0;
        craftQuantity = 0;

        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        if (!this.ContainsPoint(world, mousePos, viewportWidth, viewportHeight))
        {
            return false;
        }

        float tabY = oy + 42f;
        float tabW = 90f;
        if (mousePos.Y >= tabY && mousePos.Y <= tabY + 26f)
        {
            for (int t = 0; t < s_tabLabels.Length; t++)
            {
                RectangleF tRect = new(ox + 18f + (t * (tabW + 5f)), tabY, tabW, 26f);
                if (tRect.Contains(mousePos))
                {
                    _activeTab = t;
                    _scrollOffset = 0;
                    return true;
                }
            }

            RectangleF filterRect = GetCraftableFilterRect(ox, oy);
            if (filterRect.Contains(mousePos))
            {
                _craftableOnly = !_craftableOnly;
                _scrollOffset = 0;
                return true;
            }
        }

        if (GetScrollUpRect(ox, oy).Contains(mousePos))
        {
            this.Scroll(-1);
            return true;
        }

        if (GetScrollDownRect(ox, oy).Contains(mousePos))
        {
            this.Scroll(1);
            return true;
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        if (GetCraft1ButtonRect(ox, oy).Contains(mousePos))
        {
            if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? rec) && CanCraftFromSpan(slots, rec))
            {
                craftRecipeId = _selectedRecipeId;
                craftQuantity = 1;
            }

            return true;
        }

        if (GetCraft5ButtonRect(ox, oy).Contains(mousePos))
        {
            if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? rec) && CanCraftFromSpan(slots, rec, quantity: 5))
            {
                craftRecipeId = _selectedRecipeId;
                craftQuantity = 5;
            }

            return true;
        }

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        int matchIdx = 0;
        int shown = 0;
        for (int i = 0; i < recipes.Length && shown < MaxVisibleCards; i++)
        {
            CraftingRecipe rec = recipes[i];
            if (!this.MatchesFilter(slots, rec))
            {
                continue;
            }

            if (matchIdx < _scrollOffset)
            {
                matchIdx++;
                continue;
            }

            RectangleF rowRect = new(ox + 26f, oy + 84f + (shown * (CardRowH + 4f)), 322f, CardRowH);
            if (rowRect.Contains(mousePos))
            {
                _selectedRecipeId = (ushort)rec.Id;
                return true;
            }

            matchIdx++;
            shown++;
        }

        return true;
    }

    public void DrawSkin(
        SpriteBatch spriteBatch,
        CarbonUiSkin skin,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        PlayerModalTabs.DrawTabsSkin(spriteBatch, skin, ox, PanelW, oy, PlayerModalTab.Crafting, hasSkills, mouseScreenPos);
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelW, PanelH), CarbonUiPalette.BorderOuter, bodyAlpha: 246);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 16f, oy + 76f, 356f, 376f), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 382f, oy + 76f, 346f, 376f), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        float tabY = oy + 42f;
        float tabW = 90f;
        for (int t = 0; t < s_tabLabels.Length; t++)
        {
            RectangleF tRect = new(ox + 18f + (t * (tabW + 5f)), tabY, tabW, 26f);
            bool active = t == _activeTab;
            skin.DrawButton(
                spriteBatch,
                in tRect,
                active ? CarbonUiPalette.SlotSelectedFill : CarbonUiPalette.SlotFill,
                active ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
        }

        RectangleF filterRect = GetCraftableFilterRect(ox, oy);
        skin.DrawButton(
            spriteBatch,
            in filterRect,
            _craftableOnly ? CarbonUiPalette.ConfirmFill : CarbonUiPalette.SlotFill,
            _craftableOnly ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderSubtle);

        skin.DrawButton(spriteBatch, GetScrollUpRect(ox, oy), CarbonUiPalette.CardFill, CarbonUiPalette.BorderSubtle);
        skin.DrawButton(spriteBatch, GetScrollDownRect(ox, oy), CarbonUiPalette.CardFill, CarbonUiPalette.BorderSubtle);

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        int totalMatches = this.CountMatches(slots, recipes);
        int maxScroll = Math.Max(0, totalMatches - MaxVisibleCards);
        if (_scrollOffset > maxScroll)
        {
            _scrollOffset = maxScroll;
        }

        int matchIdx = 0;
        int shown = 0;
        for (int i = 0; i < recipes.Length && shown < MaxVisibleCards; i++)
        {
            CraftingRecipe rec = recipes[i];
            if (!this.MatchesFilter(slots, rec))
            {
                continue;
            }

            if (matchIdx < _scrollOffset)
            {
                matchIdx++;
                continue;
            }

            bool sel = rec.Id == _selectedRecipeId;
            bool canCraft = CanCraftFromSpan(slots, rec);
            RectangleF rowRect = new(ox + 26f, oy + 84f + (shown * (CardRowH + 4f)), 322f, CardRowH);
            ColorRgba rowFill = sel
                ? CarbonUiPalette.SlotSelectedFill
                : CarbonUiPalette.SlotFill;
            ColorRgba rowBorder = sel
                ? CarbonUiPalette.BorderActive
                : (canCraft ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderSubtle);
            skin.DrawSlot(spriteBatch, in rowRect, rowFill, rowBorder);

            RectangleF iconSocket = new(rowRect.X + 7f, rowRect.Y + 7f, 40f, 40f);
            skin.DrawSlot(spriteBatch, in iconSocket, CarbonUiPalette.WindowFill, CarbonUiPalette.BorderSubtle);

            matchIdx++;
            shown++;
        }

        RectangleF outIconBox = new(ox + 398f, oy + 92f, 56f, 56f);
        skin.DrawSlot(spriteBatch, in outIconBox, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderOuter);

        if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? selectedRec))
        {
            for (int k = 0; k < selectedRec.Ingredients.Count && k < 3; k++)
            {
                RecipeIngredient ing = selectedRec.Ingredients[k];
                bool hasEnough = CountBagItem(slots, ing.ItemId) >= ing.Quantity;
                RectangleF ingCard = new(ox + 398f, oy + 236f + (k * 46f), 314f, 40f);
                skin.DrawSlot(
                    spriteBatch,
                    in ingCard,
                    CarbonUiPalette.SlotFill,
                    hasEnough ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder);
            }

            bool ready = CanCraftFromSpan(slots, selectedRec);
            bool ready5 = CanCraftFromSpan(slots, selectedRec, quantity: 5);
            skin.DrawButton(
                spriteBatch,
                GetCraft1ButtonRect(ox, oy),
                ready ? CarbonUiPalette.ConfirmFill : CarbonUiPalette.SlotFill,
                ready ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderSubtle);
            skin.DrawButton(
                spriteBatch,
                GetCraft5ButtonRect(ox, oy),
                ready5 ? CarbonUiPalette.SlotSelectedFill : CarbonUiPalette.SlotFill,
                ready5 ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
        }
    }

    public void DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        int matchIdx = 0;
        int shown = 0;
        for (int i = 0; i < recipes.Length && shown < MaxVisibleCards; i++)
        {
            CraftingRecipe rec = recipes[i];
            if (!this.MatchesFilter(slots, rec))
            {
                continue;
            }

            if (matchIdx < _scrollOffset)
            {
                matchIdx++;
                continue;
            }

            RectangleF rowRect = new(ox + 26f, oy + 84f + (shown * (CardRowH + 4f)), 322f, CardRowH);
            if (art is not null && art.TryGetItem(rec.Output.ItemId, out CompiledItemVectorArt? rowArt))
            {
                VectorRigRenderer.DrawItemIcon(batch, rowArt, new Vector2(rowRect.X + 27f, rowRect.Y + 27f), 26f);
            }

            matchIdx++;
            shown++;
        }

        if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? selectedRec) && art is not null)
        {
            if (art.TryGetItem(selectedRec.Output.ItemId, out CompiledItemVectorArt? outArt))
            {
                VectorRigRenderer.DrawItemIcon(batch, outArt, new Vector2(ox + 426f, oy + 120f), 36f);
            }

            for (int k = 0; k < selectedRec.Ingredients.Count && k < 3; k++)
            {
                if (art.TryGetItem(selectedRec.Ingredients[k].ItemId, out CompiledItemVectorArt? ingArt))
                {
                    VectorRigRenderer.DrawItemIcon(batch, ingArt, new Vector2(ox + 420f, oy + 256f + (k * 46f)), 24f);
                }
            }
        }
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        PlayerModalTabs.DrawTabsText(spriteBatch, font, ox, PanelW, oy, PlayerModalTab.Crafting, hasSkills);

        ColorRgba black = CarbonUiPalette.TextOutline;
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        spriteBatch.DrawOutlinedString(font, "Crafting", new Vector2(ox + 18f, oy + 13f), CarbonUiPalette.TextWarm, black, 1, 1f);

        float tabY = oy + 42f;
        float tabW = 90f;
        for (int t = 0; t < s_tabLabels.Length; t++)
        {
            ColorRgba tc = t == _activeTab ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextPrimary;
            TextMeasureResult m = font.MeasureString(s_tabLabels[t], TextLayoutOptions.Default, 1f);
            float tx = ox + 18f + (t * (tabW + 5f)) + ((tabW - m.Width) * 0.5f);
            spriteBatch.DrawOutlinedString(font, s_tabLabels[t], new Vector2(MathF.Floor(tx), tabY + 5f), tc, black, 1, 1f);
        }

        RectangleF filterRect = GetCraftableFilterRect(ox, oy);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, _craftableOnly ? "READY" : "ALL", in filterRect, CarbonUiPalette.TextPrimary, black, 1f);

        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "^", GetScrollUpRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "v", GetScrollDownRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        int matchIdx = 0;
        int shown = 0;
        for (int i = 0; i < recipes.Length && shown < MaxVisibleCards; i++)
        {
            CraftingRecipe rec = recipes[i];
            if (!this.MatchesFilter(slots, rec))
            {
                continue;
            }

            if (matchIdx < _scrollOffset)
            {
                matchIdx++;
                continue;
            }

            RectangleF rowRect = new(ox + 26f, oy + 84f + (shown * (CardRowH + 4f)), 322f, CardRowH);
            bool canCraft = CanCraftFromSpan(slots, rec);
            string outName = CarbonUiSkin.TruncateToWidth(font, $"{StarterGameCatalog.GetDisplayName(rec.Output.ItemId)} x{rec.Output.Quantity}", 206f, 1f);
            spriteBatch.DrawOutlinedString(font, outName, new Vector2(rowRect.X + 54f, rowRect.Y + 8f), canCraft ? CarbonUiPalette.TextPrimary : CarbonUiPalette.TextMuted, black, 1, 1f);

            string summaryTag = CarbonUiSkin.TruncateToWidth(font, FormatRecipeShortTag(rec.Output.ItemId), 206f, 1f);
            spriteBatch.DrawOutlinedString(font, summaryTag, new Vector2(rowRect.X + 54f, rowRect.Y + 29f), CarbonUiPalette.TextMuted, black, 1, 1f);

            matchIdx++;
            shown++;
        }

        if (shown == 0)
        {
            spriteBatch.DrawOutlinedString(font, "No matching recipes", new Vector2(ox + 92f, oy + 220f), CarbonUiPalette.TextMuted, black, 1, 1f);
        }

        if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? selectedRec))
        {
            uint outId = selectedRec.Output.ItemId;
            int owned = CountBagItem(slots, outId);
            string title = CarbonUiSkin.TruncateToWidth(font, $"{StarterGameCatalog.GetDisplayName(outId)} x{selectedRec.Output.Quantity}", 250f, 1.25f);
            spriteBatch.DrawOutlinedString(font, title, new Vector2(ox + 464f, oy + 96f), CarbonUiPalette.TextWarm, black, 1, 1.25f);
            spriteBatch.DrawOutlinedString(font, $"Owned: {owned}", new Vector2(ox + 464f, oy + 126f), CarbonUiPalette.TextMuted, black, 1, 1f);

            spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, FormatDetailedStats(outId), 316f, 1f), new Vector2(ox + 398f, oy + 164f), CarbonUiPalette.TextPrimary, black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, "Ingredients", new Vector2(ox + 398f, oy + 212f), CarbonUiPalette.TextWarm, black, 1, 1f);
            for (int k = 0; k < selectedRec.Ingredients.Count && k < 3; k++)
            {
                RecipeIngredient ing = selectedRec.Ingredients[k];
                int have = CountBagItem(slots, ing.ItemId);
                bool ok = have >= ing.Quantity;
                float iy = oy + 246f + (k * 46f);
                spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, StarterGameCatalog.GetDisplayName(ing.ItemId), 188f, 1f), new Vector2(ox + 442f, iy), CarbonUiPalette.TextPrimary, black, 1, 1f);
                spriteBatch.DrawOutlinedString(font, $"{have}/{ing.Quantity}", new Vector2(ox + 644f, iy), ok ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder, black, 1, 1f);
            }

            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "CRAFT x1", GetCraft1ButtonRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "CRAFT x5", GetCraft5ButtonRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
        }
    }

    private int CountMatches(ReadOnlySpan<ItemSlot> slots, ReadOnlySpan<CraftingRecipe> recipes)
    {
        int count = 0;
        for (int i = 0; i < recipes.Length; i++)
        {
            if (this.MatchesFilter(slots, recipes[i]))
            {
                count++;
            }
        }

        return count;
    }

    private bool MatchesFilter(ReadOnlySpan<ItemSlot> slots, CraftingRecipe recipe)
    {
        if (_craftableOnly && !CanCraftFromSpan(slots, recipe))
        {
            return false;
        }

        if (_activeTab == 0)
        {
            return true;
        }

        if (!StarterGameCatalog.Items.TryGet(recipe.Output.ItemId, out ItemArchetype? def))
        {
            return false;
        }

        return _activeTab switch
        {
            1 => def.Category == ItemCategory.Weapon,
            2 => def.Category == ItemCategory.Armor,
            3 => def.Category == ItemCategory.Tool,
            4 => def.Category == ItemCategory.Placeable,
            5 => def.Category == ItemCategory.Consumable,
            _ => true,
        };
    }

    private static string FormatRecipeShortTag(uint itemId) =>
        StarterGameCatalog.Items.TryGet(itemId, out ItemArchetype? def)
            ? def.Category switch
            {
                ItemCategory.Weapon => $"Weapon  {(int)def.BaseDamage} DMG",
                ItemCategory.Armor => $"Armor  +{(int)def.ArmorDefense} DEF",
                ItemCategory.Tool => $"Tool  Tier {def.HarvestTier}",
                ItemCategory.Placeable => "Structure",
                ItemCategory.Consumable => "Consumable",
                ItemCategory.Accessory => "Accessory",
                ItemCategory.Quest => "Quest Item",
                ItemCategory.Resource or ItemCategory.None or _ => "Material",
            }
            : string.Empty;

    private static string FormatDetailedStats(uint itemId) =>
        StarterGameCatalog.Items.TryGet(itemId, out ItemArchetype? def)
            ? def.Category switch
            {
                ItemCategory.Weapon => $"DMG {(int)def.BaseDamage}  Crit {(int)(def.CritChance * 100f)}%  RNG {(int)def.AttackRange}",
                ItemCategory.Armor => $"DEF +{(int)def.ArmorDefense}  HP +{(int)def.BonusHealth}  MP +{(int)def.BonusMana}",
                ItemCategory.Tool => $"Tier {def.HarvestTier}  Chop {(int)def.FloraHarvestDamage}  Mine {(int)def.MineralHarvestDamage}",
                ItemCategory.Consumable or ItemCategory.Placeable or ItemCategory.Accessory or ItemCategory.Resource or ItemCategory.Quest or ItemCategory.None or _ => $"Max Stack {def.MaxStackSize}",
            }
            : string.Empty;

    private static int CountBagItem(ReadOnlySpan<ItemSlot> slots, uint itemId)
    {
        int total = 0;
        int bagCount = Math.Min(PlayerInventoryLayout.BagLimit, slots.Length);
        for (int i = 0; i < bagCount; i++)
        {
            if (slots[i].ItemId == itemId && slots[i].UpgradeLevel == 0)
            {
                total += slots[i].Quantity;
            }
        }

        return total;
    }

    private static bool CanCraftFromSpan(ReadOnlySpan<ItemSlot> slots, CraftingRecipe recipe, int quantity = 1)
    {
        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            RecipeIngredient ing = recipe.Ingredients[i];
            if (CountBagItem(slots, ing.ItemId) < ing.Quantity * quantity)
            {
                return false;
            }
        }

        return true;
    }

    private static (float Ox, float Oy) ComputeOrigin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelW) * 0.5f), MathF.Max(42f, MathF.Floor((viewportHeight - PanelH) * 0.5f)));

    private static RectangleF GetCraftableFilterRect(float ox, float oy) => new(ox + 602f, oy + 42f, 124f, 26f);

    private static RectangleF GetScrollUpRect(float ox, float oy) => new(ox + 350f, oy + 84f, 18f, 22f);

    private static RectangleF GetScrollDownRect(float ox, float oy) => new(ox + 350f, oy + 392f, 18f, 22f);

    private static RectangleF GetCraft1ButtonRect(float ox, float oy) => new(ox + 398f, oy + 394f, 156f, 38f);

    private static RectangleF GetCraft5ButtonRect(float ox, float oy) => new(ox + 562f, oy + 394f, 150f, 38f);
}

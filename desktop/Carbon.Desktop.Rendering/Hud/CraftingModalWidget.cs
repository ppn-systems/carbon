// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.World;
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
/// Two-pane Crafting Workshop tab (<c>[C]</c>) framed inside the fixed
/// <see cref="PlayerModalTabs"/> workspace (SRP).
/// </summary>
internal sealed class CraftingModalWidget
{
    private const int MaxVisibleCards = 6;

    private static readonly string[] s_tabLabels = ["ALL", "WEAPONS", "ARMOR", "TOOLS", "BUILD", "POTIONS"];

    private readonly List<SkillDefinition> _skillScratch = new(8);
    private int _activeTab;
    private int _scrollOffset;
    private ushort _selectedRecipeId = 1;
    private bool _craftableOnly;

    public bool ContainsPoint(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        _ = world;
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        return PlayerModalTabs.ContainsWindowOrTabs(in layout, mousePos);
    }

    public bool TryHitTestCloseButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        return PlayerModalTabs.GetCloseButtonRect(in layout).Contains(mousePos);
    }

    public bool TryHitTestTab(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight, out PlayerModalTab tab)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        return PlayerModalTabs.TryHitTestTab(mousePos, in layout, hasSkills, out tab);
    }

    public void Scroll(int delta) => _scrollOffset = Math.Max(0, _scrollOffset + delta);

    public bool TryHandleClick(
        Vector2 mousePos,
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        out ushort craftRecipeId,
        out ushort craftQuantity)
    {
        craftRecipeId = 0;
        craftQuantity = 0;

        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        if (!this.ContainsPoint(world, mousePos, viewportWidth, viewportHeight))
        {
            return false;
        }

        for (int t = 0; t < s_tabLabels.Length; t++)
        {
            if (GetFilterTabRect(in layout, t).Contains(mousePos))
            {
                _activeTab = t;
                _scrollOffset = 0;
                return true;
            }
        }

        if (GetCraftableFilterRect(in layout).Contains(mousePos))
        {
            _craftableOnly = !_craftableOnly;
            _scrollOffset = 0;
            return true;
        }

        if (GetScrollUpRect(in layout).Contains(mousePos))
        {
            this.Scroll(-1);
            return true;
        }

        if (GetScrollDownRect(in layout).Contains(mousePos))
        {
            this.Scroll(1);
            return true;
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        if (GetCraft1ButtonRect(in layout).Contains(mousePos))
        {
            if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? rec) && PlayerInventoryLayout.CanCraft(slots, rec))
            {
                craftRecipeId = _selectedRecipeId;
                craftQuantity = 1;
            }

            return true;
        }

        if (GetCraft5ButtonRect(in layout).Contains(mousePos))
        {
            if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? rec) && PlayerInventoryLayout.CanCraft(slots, rec, quantity: 5))
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

            if (GetRecipeRowRect(in layout, shown).Contains(mousePos))
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
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        PlayerModalTabs.DrawWindowChromeSkin(spriteBatch, skin, in layout, PlayerModalTab.Crafting, hasSkills, mouseScreenPos);

        RectangleF leftPane = GetLeftPaneRect(in layout);
        RectangleF rightPane = GetRightPaneRect(in layout);
        skin.DrawCard(spriteBatch, in leftPane, CarbonUiPalette.BorderSubtle, bodyAlpha: 236);
        skin.DrawCard(spriteBatch, in rightPane, CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        for (int t = 0; t < s_tabLabels.Length; t++)
        {
            RectangleF tRect = GetFilterTabRect(in layout, t);
            bool active = t == _activeTab;
            skin.DrawButton(
                spriteBatch,
                in tRect,
                active ? CarbonUiPalette.SlotSelectedFill : CarbonUiPalette.SlotFill,
                active ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
        }

        RectangleF filterRect = GetCraftableFilterRect(in layout);
        skin.DrawButton(
            spriteBatch,
            in filterRect,
            _craftableOnly ? CarbonUiPalette.ConfirmFill : CarbonUiPalette.SlotFill,
            _craftableOnly ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderSubtle);

        skin.DrawButton(spriteBatch, GetScrollUpRect(in layout), CarbonUiPalette.CardFill, CarbonUiPalette.BorderSubtle);
        skin.DrawButton(spriteBatch, GetScrollDownRect(in layout), CarbonUiPalette.CardFill, CarbonUiPalette.BorderSubtle);

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
            bool canCraft = PlayerInventoryLayout.CanCraft(slots, rec);
            RectangleF rowRect = GetRecipeRowRect(in layout, shown);
            ColorRgba rowFill = sel ? CarbonUiPalette.SlotSelectedFill : CarbonUiPalette.SlotFill;
            ColorRgba rowBorder = sel
                ? CarbonUiPalette.BorderActive
                : (canCraft ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderSubtle);
            skin.DrawSlot(spriteBatch, in rowRect, rowFill, rowBorder);

            float iconBox = MathF.Round(48f * s);
            RectangleF iconSocket = new(rowRect.X + MathF.Round(8f * s), rowRect.Y + MathF.Round(9f * s), iconBox, iconBox);
            skin.DrawSlot(spriteBatch, in iconSocket, CarbonUiPalette.WindowFill, CarbonUiPalette.BorderSubtle);

            matchIdx++;
            shown++;
        }

        RectangleF outIconBox = GetOutputIconRect(in layout);
        skin.DrawSlot(spriteBatch, in outIconBox, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderOuter);

        if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? selectedRec))
        {
            for (int k = 0; k < selectedRec.Ingredients.Count; k++)
            {
                RecipeIngredient ing = selectedRec.Ingredients[k];
                bool hasEnough = PlayerInventoryLayout.CountItem(slots, ing.ItemId) >= ing.Quantity;
                RectangleF ingCard = GetIngredientRect(in layout, k, selectedRec.Ingredients.Count);
                skin.DrawSlot(
                    spriteBatch,
                    in ingCard,
                    CarbonUiPalette.SlotFill,
                    hasEnough ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder);
            }

            bool ready = PlayerInventoryLayout.CanCraft(slots, selectedRec);
            bool ready5 = PlayerInventoryLayout.CanCraft(slots, selectedRec, quantity: 5);
            skin.DrawButton(
                spriteBatch,
                GetCraft1ButtonRect(in layout),
                ready ? CarbonUiPalette.ConfirmFill : CarbonUiPalette.SlotFill,
                ready ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderSubtle);
            skin.DrawButton(
                spriteBatch,
                GetCraft5ButtonRect(in layout),
                ready5 ? CarbonUiPalette.SlotSelectedFill : CarbonUiPalette.SlotFill,
                ready5 ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
        }
    }

    public void DrawGeometry(
        GeometryBatch batch,
        IWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;
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

            RectangleF rowRect = GetRecipeRowRect(in layout, shown);
            if (art is not null && art.TryGetItem(rec.Output.ItemId, out CompiledItemVectorArt? rowArt))
            {
                VectorRigRenderer.DrawItemIcon(
                    batch,
                    rowArt,
                    new Vector2(rowRect.X + MathF.Round(32f * s), rowRect.Y + MathF.Round(33f * s)),
                    MathF.Round(34f * s));
            }

            matchIdx++;
            shown++;
        }

        if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? selectedRec) && art is not null)
        {
            RectangleF outBox = GetOutputIconRect(in layout);
            if (art.TryGetItem(selectedRec.Output.ItemId, out CompiledItemVectorArt? outArt))
            {
                VectorRigRenderer.DrawItemIcon(
                    batch,
                    outArt,
                    new Vector2(outBox.X + (outBox.Width * 0.5f), outBox.Y + (outBox.Height * 0.5f)),
                    MathF.Round(46f * s));
            }

            for (int k = 0; k < selectedRec.Ingredients.Count; k++)
            {
                if (art.TryGetItem(selectedRec.Ingredients[k].ItemId, out CompiledItemVectorArt? ingArt))
                {
                    RectangleF ingCard = GetIngredientRect(in layout, k, selectedRec.Ingredients.Count);
                    VectorRigRenderer.DrawItemIcon(
                        batch,
                        ingArt,
                        new Vector2(ingCard.X + MathF.Round(24f * s), ingCard.Y + (ingCard.Height * 0.5f)),
                        MathF.Min(MathF.Round(30f * s), ingCard.Height - MathF.Round(6f * s)));
                }
            }
        }
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        PlayerModalTabs.DrawWindowChromeText(spriteBatch, font, in layout, PlayerModalTab.Crafting, hasSkills, "Crafting");

        ColorRgba black = CarbonUiPalette.TextOutline;
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        for (int t = 0; t < s_tabLabels.Length; t++)
        {
            RectangleF tRect = GetFilterTabRect(in layout, t);
            ColorRgba tc = t == _activeTab ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextPrimary;
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, s_tabLabels[t], in tRect, tc, black, layout.TextScale);
        }

        RectangleF filterRect = GetCraftableFilterRect(in layout);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, _craftableOnly ? "READY" : "ALL", in filterRect, CarbonUiPalette.TextPrimary, black, layout.TextScale);

        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "^", GetScrollUpRect(in layout), CarbonUiPalette.TextPrimary, black, layout.TextScale);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "v", GetScrollDownRect(in layout), CarbonUiPalette.TextPrimary, black, layout.TextScale);

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

            RectangleF rowRect = GetRecipeRowRect(in layout, shown);
            bool canCraft = PlayerInventoryLayout.CanCraft(slots, rec);
            float textMaxW = rowRect.Width - MathF.Round(72f * s);
            string outName = CarbonUiSkin.TruncateToWidth(font, $"{SyncedItemCatalog.GetDisplayName(rec.Output.ItemId)} x{rec.Output.Quantity}", textMaxW, layout.TextScale);
            spriteBatch.DrawOutlinedString(font, outName, new Vector2(rowRect.X + MathF.Round(64f * s), rowRect.Y + MathF.Round(10f * s)), canCraft ? CarbonUiPalette.TextPrimary : CarbonUiPalette.TextMuted, black, 1, layout.TextScale);

            string summaryTag = CarbonUiSkin.TruncateToWidth(font, FormatRecipeShortTag(rec.Output.ItemId), textMaxW, layout.TextScale);
            spriteBatch.DrawOutlinedString(font, summaryTag, new Vector2(rowRect.X + MathF.Round(64f * s), rowRect.Y + MathF.Round(36f * s)), CarbonUiPalette.TextMuted, black, 1, layout.TextScale);

            matchIdx++;
            shown++;
        }

        if (shown == 0)
        {
            RectangleF leftPane = GetLeftPaneRect(in layout);
            spriteBatch.DrawOutlinedString(font, "No matching recipes", new Vector2(leftPane.X + MathF.Round(72f * s), leftPane.Y + MathF.Round(190f * s)), CarbonUiPalette.TextMuted, black, 1, layout.TextScale);
        }

        if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? selectedRec))
        {
            RectangleF rightPane = GetRightPaneRect(in layout);
            RectangleF outBox = GetOutputIconRect(in layout);
            uint outId = selectedRec.Output.ItemId;
            int owned = PlayerInventoryLayout.CountItem(slots, outId);
            float headerX = outBox.Right + MathF.Round(12f * s);
            float headerMaxW = rightPane.Right - headerX - MathF.Round(14f * s);
            string title = CarbonUiSkin.TruncateToWidth(font, $"{SyncedItemCatalog.GetDisplayName(outId)} x{selectedRec.Output.Quantity}", headerMaxW, layout.TitleScale);
            spriteBatch.DrawOutlinedString(font, title, new Vector2(headerX, outBox.Y + MathF.Round(4f * s)), CarbonUiPalette.TextWarm, black, 1, layout.TitleScale);
            string requirement = CarbonUiSkin.TruncateToWidth(font, $"Owned: {owned}  |  {FormatRequirement(selectedRec)}", headerMaxW, layout.TextScale);
            spriteBatch.DrawOutlinedString(font, requirement, new Vector2(headerX, outBox.Y + MathF.Round(38f * s)), CarbonUiPalette.TextMuted, black, 1, layout.TextScale);

            float detailPad = MathF.Round(16f * s);
            spriteBatch.DrawOutlinedString(
                font,
                CarbonUiSkin.TruncateToWidth(font, FormatDetailedStats(outId), rightPane.Width - (detailPad * 2f), layout.TextScale),
                new Vector2(rightPane.X + detailPad, outBox.Bottom + MathF.Round(18f * s)),
                CarbonUiPalette.TextPrimary,
                black,
                1,
                layout.TextScale);
            spriteBatch.DrawOutlinedString(font, "Ingredients", new Vector2(rightPane.X + detailPad, outBox.Bottom + MathF.Round(54f * s)), CarbonUiPalette.TextWarm, black, 1, layout.TextScale);

            for (int k = 0; k < selectedRec.Ingredients.Count; k++)
            {
                RecipeIngredient ing = selectedRec.Ingredients[k];
                int have = PlayerInventoryLayout.CountItem(slots, ing.ItemId);
                bool ok = have >= ing.Quantity;
                RectangleF ingCard = GetIngredientRect(in layout, k, selectedRec.Ingredients.Count);
                float textY = ingCard.Y + MathF.Max(0f, (ingCard.Height * 0.5f) - MathF.Round(10f * s));
                spriteBatch.DrawOutlinedString(
                    font,
                    CarbonUiSkin.TruncateToWidth(font, SyncedItemCatalog.GetDisplayName(ing.ItemId), ingCard.Width - MathF.Round(120f * s), layout.TextScale),
                    new Vector2(ingCard.X + MathF.Round(48f * s), textY),
                    CarbonUiPalette.TextPrimary,
                    black,
                    1,
                    layout.TextScale);
                string countStr = $"{have}/{ing.Quantity}";
                TextMeasureResult cSize = font.MeasureString(countStr, TextLayoutOptions.Default, layout.TextScale);
                spriteBatch.DrawOutlinedString(
                    font,
                    countStr,
                    new Vector2(ingCard.Right - cSize.Width - MathF.Round(12f * s), textY),
                    ok ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder,
                    black,
                    1,
                    layout.TextScale);
            }

            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "CRAFT x1", GetCraft1ButtonRect(in layout), CarbonUiPalette.TextPrimary, black, layout.TextScale);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "CRAFT x5", GetCraft5ButtonRect(in layout), CarbonUiPalette.TextPrimary, black, layout.TextScale);
        }
    }

    /// <summary>Formats the recipes.json station and unlock-level gate the server enforces (e.g. <c>Forge Lv 12</c>).</summary>
    private static string FormatRequirement(CraftingRecipe recipe)
    {
        string station = recipe.Station switch
        {
            CraftingStation.None => "Hand",
            CraftingStation.Workbench => "Workbench",
            CraftingStation.Forge => "Forge",
            CraftingStation.Stove => "Stove/Campfire",
            CraftingStation.Alchemy => "Shrine",
            _ => recipe.Station.ToString(),
        };

        return recipe.UnlockLevel > 1 ? $"{station} Lv {recipe.UnlockLevel}" : station;
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
        if (_craftableOnly && !PlayerInventoryLayout.CanCraft(slots, recipe))
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

    private static RectangleF GetFilterTabRect(in PlayerModalLayout layout, int tabIndex)
    {
        float s = layout.Scale;
        float filterW = MathF.Round(108f * s);
        float gap = MathF.Round(5f * s);
        float catsTotalW = layout.ContentW - filterW - MathF.Round(10f * s);
        float tabW = MathF.Floor((catsTotalW - ((s_tabLabels.Length - 1) * gap)) / s_tabLabels.Length);
        float x = layout.ContentX + (tabIndex * (tabW + gap));
        float w = tabIndex == s_tabLabels.Length - 1 ? layout.ContentX + catsTotalW - x : tabW;
        return new RectangleF(x, layout.ContentY, w, MathF.Round(32f * s));
    }

    private static RectangleF GetCraftableFilterRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        float filterW = MathF.Round(108f * s);
        return new RectangleF(layout.ContentX + layout.ContentW - filterW, layout.ContentY, filterW, MathF.Round(32f * s));
    }

    private static RectangleF GetLeftPaneRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        float topOffset = MathF.Round(42f * s);
        return new RectangleF(layout.ContentX, layout.ContentY + topOffset, MathF.Round(344f * s), layout.ContentH - topOffset);
    }

    private static RectangleF GetRightPaneRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        float topOffset = MathF.Round(42f * s);
        float leftW = MathF.Round(344f * s);
        float gap = MathF.Round(14f * s);
        return new RectangleF(layout.ContentX + leftW + gap, layout.ContentY + topOffset, layout.ContentW - leftW - gap, layout.ContentH - topOffset);
    }

    private static RectangleF GetOutputIconRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF rightPane = GetRightPaneRect(in layout);
        float size = MathF.Round(68f * s);
        return new RectangleF(rightPane.X + MathF.Round(16f * s), rightPane.Y + MathF.Round(16f * s), size, size);
    }

    private static RectangleF GetRecipeRowRect(in PlayerModalLayout layout, int shownIndex)
    {
        float s = layout.Scale;
        RectangleF leftPane = GetLeftPaneRect(in layout);
        float pad = MathF.Round(10f * s);
        float scrollW = MathF.Round(24f * s);
        float rowW = leftPane.Width - (pad * 2f) - scrollW - MathF.Round(8f * s);
        return new RectangleF(
            leftPane.X + pad,
            leftPane.Y + pad + (shownIndex * MathF.Round(72f * s)),
            rowW,
            MathF.Round(66f * s));
    }

    private static RectangleF GetScrollUpRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF leftPane = GetLeftPaneRect(in layout);
        float pad = MathF.Round(10f * s);
        float scrollW = MathF.Round(24f * s);
        return new RectangleF(leftPane.Right - pad - scrollW, leftPane.Y + pad, scrollW, MathF.Round(28f * s));
    }

    private static RectangleF GetScrollDownRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF leftPane = GetLeftPaneRect(in layout);
        float pad = MathF.Round(10f * s);
        float scrollW = MathF.Round(24f * s);
        float scrollH = MathF.Round(28f * s);
        return new RectangleF(leftPane.Right - pad - scrollW, leftPane.Bottom - pad - scrollH, scrollW, scrollH);
    }

    /// <summary>
    /// Lays out ingredient row <paramref name="index"/> of <paramref name="count"/> between the stats line and the craft
    /// buttons. Rows keep their natural 48px height (56px pitch) and shrink evenly only when every row would not fit.
    /// </summary>
    internal static RectangleF GetIngredientRect(in PlayerModalLayout layout, int index, int count)
    {
        float s = layout.Scale;
        RectangleF rightPane = GetRightPaneRect(in layout);
        float pad = MathF.Round(16f * s);
        float top = rightPane.Y + MathF.Round(168f * s);
        float bottom = GetCraft1ButtonRect(in layout).Y - MathF.Round(10f * s);
        float gap = MathF.Round(8f * s);
        float naturalPitch = MathF.Round(56f * s);
        float pitch = MathF.Floor(MathF.Min(naturalPitch, (bottom - top + gap) / Math.Max(1, count)));
        return new RectangleF(
            rightPane.X + pad,
            top + (index * pitch),
            rightPane.Width - (pad * 2f),
            MathF.Max(1f, pitch - gap));
    }

    internal static RectangleF GetCraft1ButtonRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF rightPane = GetRightPaneRect(in layout);
        float pad = MathF.Round(16f * s);
        float gap = MathF.Round(10f * s);
        float btnW = MathF.Floor((rightPane.Width - (pad * 2f) - gap) * 0.5f);
        float btnH = MathF.Round(46f * s);
        return new RectangleF(rightPane.X + pad, rightPane.Bottom - pad - btnH, btnW, btnH);
    }

    private static RectangleF GetCraft5ButtonRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF rightPane = GetRightPaneRect(in layout);
        float pad = MathF.Round(16f * s);
        float gap = MathF.Round(10f * s);
        float btnW = MathF.Floor((rightPane.Width - (pad * 2f) - gap) * 0.5f);
        float btnH = MathF.Round(46f * s);
        return new RectangleF(rightPane.Right - pad - btnW, rightPane.Bottom - pad - btnH, btnW, btnH);
    }
}

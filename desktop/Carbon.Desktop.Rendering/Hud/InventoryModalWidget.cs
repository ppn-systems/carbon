// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Pure 40-slot MMORPG Bag, Armor Paper-Doll (<c>36..39</c>), Character Attributes Sheet,
/// and Selected Item Inspector (<c>[EQUIP / USE]</c>, <c>[DROP 1]</c>, <c>[DROP ALL]</c>)
/// skinned cleanly with <see cref="CarbonUiSkin"/> — decoupled from Blacksmith Forging (SRP).
/// </summary>
internal sealed class InventoryModalWidget
{
    private const float PanelW = 700f;
    private const float PanelH = 484f;
    private const float SlotSize = 44f;
    private const float SlotGap = 6f;

    private static readonly string[] s_armorLabels = ["HEAD", "CHEST", "LEGS", "FEET"];

    public int SelectedSlot { get; set; } = -1;

    public int DraggedSlot { get; set; } = -1;

    public bool ContainsPoint(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return mousePos.X >= ox && mousePos.X <= ox + PanelW && mousePos.Y >= oy && mousePos.Y <= oy + PanelH;
    }

    public bool TryHitTestCloseButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return GetCloseButtonRect(ox, oy).Contains(mousePos);
    }

    public bool TryHitTestSlot(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort slotIndex)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity; s++)
        {
            if (GetSlotRect(ox, oy, s).Contains(mousePos))
            {
                slotIndex = (ushort)s;
                return true;
            }
        }

        slotIndex = 0;
        return false;
    }

    public bool TryHitTestUseEquipButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return GetUseEquipButtonRect(ox, oy).Contains(mousePos);
    }

    public bool TryHitTestDropButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return GetDropButtonRect(ox, oy).Contains(mousePos);
    }

    public bool TryHitTestDropAllButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return GetDropAllButtonRect(ox, oy).Contains(mousePos);
    }

    public int ResolveActionTargetSlot(IClientWorldReadModel world)
    {
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        if (this.SelectedSlot >= 0 && this.SelectedSlot < slots.Length && !slots[this.SelectedSlot].IsEmpty)
        {
            return this.SelectedSlot;
        }

        if (world.SelectedHotbarSlot < slots.Length && !slots[world.SelectedHotbarSlot].IsEmpty)
        {
            return world.SelectedHotbarSlot;
        }

        return -1;
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

        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelW, PanelH), new ColorRgba(250, 204, 21, 238), bodyAlpha: 242);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 16f, oy + 40f, 256f, 204f), new ColorRgba(71, 85, 105, 225), bodyAlpha: 232);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 282f, oy + 40f, 402f, 204f), new ColorRgba(56, 189, 248, 195), bodyAlpha: 232);

        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        bool hoverClose = mouseScreenPos.HasValue && closeBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(spriteBatch, in closeBtn, hoverClose ? new ColorRgba(220, 38, 38, 245) : new ColorRgba(153, 27, 27, 230), new ColorRgba(248, 113, 113, 240));

        int targetSlot = this.ResolveActionTargetSlot(world);
        bool hasItem = targetSlot >= 0 && targetSlot < slots.Length && !slots[targetSlot].IsEmpty;

        RectangleF useBtn = GetUseEquipButtonRect(ox, oy);
        bool hoverUse = mouseScreenPos.HasValue && useBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(
            spriteBatch,
            in useBtn,
            hasItem ? (hoverUse ? new ColorRgba(16, 185, 129, 245) : new ColorRgba(5, 150, 105, 235)) : new ColorRgba(24, 34, 52, 215),
            hasItem ? new ColorRgba(110, 231, 183, 245) : new ColorRgba(71, 85, 105, 210));

        RectangleF dropBtn = GetDropButtonRect(ox, oy);
        bool hoverDrop = mouseScreenPos.HasValue && dropBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(
            spriteBatch,
            in dropBtn,
            hasItem ? (hoverDrop ? new ColorRgba(220, 38, 38, 245) : new ColorRgba(153, 27, 27, 235)) : new ColorRgba(24, 34, 52, 215),
            hasItem ? new ColorRgba(248, 113, 113, 245) : new ColorRgba(71, 85, 105, 210));

        RectangleF dropAllBtn = GetDropAllButtonRect(ox, oy);
        bool hoverDropAll = mouseScreenPos.HasValue && dropAllBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(
            spriteBatch,
            in dropAllBtn,
            hasItem ? (hoverDropAll ? new ColorRgba(185, 28, 28, 245) : new ColorRgba(127, 29, 29, 235)) : new ColorRgba(24, 34, 52, 215),
            hasItem ? new ColorRgba(248, 113, 113, 245) : new ColorRgba(71, 85, 105, 210));

        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity && s < slots.Length; s++)
        {
            RectangleF rect = GetSlotRect(ox, oy, s);
            bool hovered = mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value);
            bool selected = s == this.SelectedSlot;
            bool isArmor = s >= PlayerInventoryLayout.BagLimit;

            ColorRgba fill = selected
                ? new ColorRgba(38, 52, 84, 248)
                : (hovered ? new ColorRgba(24, 36, 60, 245) : (isArmor ? new ColorRgba(16, 26, 46, 238) : new ColorRgba(12, 18, 34, 236)));
            ColorRgba border = selected
                ? new ColorRgba(250, 204, 21, 255)
                : (hovered ? ColorRgba.White : (isArmor ? new ColorRgba(148, 163, 184, 225) : new ColorRgba(71, 85, 105, 225)));
            skin.DrawSlot(spriteBatch, in rect, fill, border);
        }
    }

    public int DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos,
        float animTime)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        if (art is not null && art.TryGetCharacter(world.LocalPlayerAppearancePreset, out CompiledVectorRig? heroRig))
        {
            ItemSlot activeItem = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;
            CompiledItemVectorArt? weaponArt = null;
            if (!activeItem.IsEmpty)
            {
                _ = art.TryGetItem(activeItem.ItemId, out weaponArt);
            }

            VisualEquipmentLoadout loadout = new(
                PlayerInventoryLayout.HeadSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.HeadSlot].ItemId : (ushort)0,
                PlayerInventoryLayout.ChestSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.ChestSlot].ItemId : (ushort)0,
                PlayerInventoryLayout.LegsSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.LegsSlot].ItemId : (ushort)0,
                PlayerInventoryLayout.FeetSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.FeetSlot].ItemId : (ushort)0,
                (ushort)activeItem.ItemId,
                ItemEnhancementPolicy.GetUpgradeLevel(in activeItem));

            Vector2 pedPos = new(ox + 178f, oy + 190f);
            batch.FillCircle(pedPos + new Vector2(0f, -6f), 32f, new ColorRgba(4, 8, 16, 130), 18);
            batch.DrawCircle(pedPos + new Vector2(0f, -6f), 33f, new ColorRgba(56, 189, 248, 185), 18, 1.5f);
            VectorRigRenderer.DrawEntityRig(
                batch,
                heroRig,
                pedPos,
                Direction8.South,
                "idle",
                animTime,
                isNormalized: false,
                targetHeightOverride: 78f,
                hitFlash: 0f,
                equippedItem: weaponArt,
                action: default,
                legacySwingTimer: 0f,
                appearancePreset: world.LocalPlayerAppearancePreset,
                equipment: loadout,
                art: art);
        }

        // Inspector item preview box
        int targetSlot = this.ResolveActionTargetSlot(world);
        ItemSlot inspected = targetSlot >= 0 && targetSlot < slots.Length ? slots[targetSlot] : ItemSlot.Empty;
        RectangleF inspBox = new(ox + 492f, oy + 62f, 48f, 48f);
        batch.FillRectangle(inspBox, new ColorRgba(12, 18, 34, 245));
        batch.DrawRectangle(inspBox, inspected.IsEmpty ? new ColorRgba(71, 85, 105, 210) : new ColorRgba(250, 204, 21, 235), 1.5f);
        if (!inspected.IsEmpty && art is not null && art.TryGetItem(inspected.ItemId, out CompiledItemVectorArt? inspArt))
        {
            VectorRigRenderer.DrawItemIcon(batch, inspArt, new Vector2(inspBox.X + 24f, inspBox.Y + 24f), 30f);
        }

        int hoveredSlot = -1;
        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity && s < slots.Length; s++)
        {
            RectangleF rect = GetSlotRect(ox, oy, s);
            if (mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value))
            {
                hoveredSlot = s;
            }

            ItemSlot item = slots[s];
            if (!item.IsEmpty)
            {
                if (art is not null && art.TryGetItem(item.ItemId, out CompiledItemVectorArt? itemArt) && s != this.DraggedSlot)
                {
                    VectorRigRenderer.DrawItemIcon(batch, itemArt, new Vector2(rect.X + (SlotSize * 0.5f), rect.Y + (SlotSize * 0.46f)), 26f);
                }

                if (StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? def) && def.MaxDurability > 0f)
                {
                    byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                    float effMax = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(def, upg));
                    float frac = Math.Clamp(item.Durability / effMax, 0f, 1f);
                    ColorRgba dCol = frac <= 0f
                        ? new ColorRgba(239, 68, 68, 255)
                        : (frac > 0.5f ? new ColorRgba(56, 189, 248, 240) : new ColorRgba(250, 204, 21, 245));
                    batch.FillRectangle(new RectangleF(rect.X + 5f, rect.Y + SlotSize - 6f, SlotSize - 10f, 3f), new ColorRgba(8, 12, 20, 235));
                    batch.FillRectangle(new RectangleF(rect.X + 5f, rect.Y + SlotSize - 6f, MathF.Max(1f, (SlotSize - 10f) * frac), 3f), dCol);
                }
            }
        }

        return hoveredSlot;
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        ColorRgba black = new(8, 10, 16, 255);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        spriteBatch.DrawOutlinedString(font, "INVENTORY & CHARACTER EQUIPMENT [I / B]", new Vector2(ox + 18f, oy + 14f), new ColorRgba(250, 204, 21, 255), black, 1, 1.5f);
        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, "X", new Vector2(closeBtn.X + 8f, closeBtn.Y + 4f), ColorRgba.White, black, 1, 1.5f);

        // Left Character Stats summary inside inspector card
        spriteBatch.DrawOutlinedString(font, "HERO ATTRIBUTES", new Vector2(ox + 294f, oy + 46f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"LEVEL : {world.LocalPlayerLevel}", new Vector2(ox + 294f, oy + 66f), ColorRgba.White, black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"HP    : {(int)MathF.Ceiling(world.LocalPlayerHealth)}/{(int)MathF.Ceiling(world.LocalPlayerMaxHealth)}", new Vector2(ox + 294f, oy + 82f), new ColorRgba(248, 113, 113, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"MANA  : {(int)MathF.Floor(world.LocalPlayerMana)}/{(int)MathF.Ceiling(world.LocalPlayerMaxMana)}", new Vector2(ox + 294f, oy + 98f), new ColorRgba(96, 165, 250, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"STAM  : {(int)MathF.Floor(world.LocalPlayerStamina)}/{(int)MathF.Ceiling(world.LocalPlayerMaxStamina)}", new Vector2(ox + 294f, oy + 114f), new ColorRgba(52, 211, 153, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"DEF   : {(int)MathF.Round(world.LocalPlayerDefense)}", new Vector2(ox + 294f, oy + 130f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"XP    : {world.LocalPlayerExperience}/{world.LocalPlayerXpToNext}", new Vector2(ox + 294f, oy + 146f), new ColorRgba(192, 132, 252, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, "Forge Gear at Blacksmith [U]", new Vector2(ox + 294f, oy + 220f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);

        // Right Selected Item Inspector
        int targetSlot = this.ResolveActionTargetSlot(world);
        ItemSlot insp = targetSlot >= 0 && targetSlot < slots.Length ? slots[targetSlot] : ItemSlot.Empty;
        spriteBatch.DrawOutlinedString(font, "ITEM INSPECTOR", new Vector2(ox + 492f, oy + 46f), new ColorRgba(250, 204, 21, 255), black, 1, 1f);
        if (insp.IsEmpty)
        {
            spriteBatch.DrawOutlinedString(font, "Select any slot", new Vector2(ox + 548f, oy + 78f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, "Shift+Click / RMB equips", new Vector2(ox + 492f, oy + 122f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
        }
        else
        {
            byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in insp);
            string iName = StarterGameCatalog.GetDisplayName(insp.ItemId);
            spriteBatch.DrawOutlinedString(font, upg > 0 ? $"{iName} +{upg}" : iName, new Vector2(ox + 548f, oy + 68f), ColorRgba.White, black, 1, 1f);
            float upgMult = ItemEnhancementPolicy.GetStatMultiplier(upg);
            string statStr = StarterGameCatalog.Items.TryGet(insp.ItemId, out ItemArchetype? def)
                ? (def.Category == ItemCategory.Armor
                    ? $"DEF +{(int)MathF.Round(def.ArmorDefense * upgMult)} HP +{(int)MathF.Round(def.BonusHealth * upgMult)}"
                    : def.Category == ItemCategory.Weapon
                        ? $"DMG {(int)MathF.Round(def.BaseDamage * upgMult)} RNG {(int)def.AttackRange}"
                        : def.Category == ItemCategory.Tool
                            ? $"Tier {def.HarvestTier} Harvest"
                            : $"Stack x{insp.Quantity}")
                : string.Empty;
            spriteBatch.DrawOutlinedString(font, statStr, new Vector2(ox + 548f, oy + 86f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);
        }

        RectangleF useBtn = GetUseEquipButtonRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, "EQUIP / MOVE", new Vector2(useBtn.X + 8f, useBtn.Y + 8f), ColorRgba.White, black, 1, 1f);
        RectangleF dropBtn = GetDropButtonRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, "DROP 1", new Vector2(dropBtn.X + 12f, dropBtn.Y + 8f), ColorRgba.White, black, 1, 1f);
        RectangleF dropAllBtn = GetDropAllButtonRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, "ALL", new Vector2(dropAllBtn.X + 12f, dropAllBtn.Y + 8f), ColorRgba.White, black, 1, 1f);

        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity && s < slots.Length; s++)
        {
            RectangleF rect = GetSlotRect(ox, oy, s);
            ItemSlot item = slots[s];

            if (s < 9)
            {
                spriteBatch.DrawOutlinedString(font, $"{s + 1}", new Vector2(rect.X + 4f, rect.Y + 4f), new ColorRgba(148, 163, 184, 235), black, 1, 1f);
            }
            else if (s >= PlayerInventoryLayout.BagLimit && item.IsEmpty)
            {
                string lbl = s_armorLabels[s - PlayerInventoryLayout.BagLimit];
                spriteBatch.DrawOutlinedString(font, lbl, new Vector2(rect.X + 6f, rect.Y + 18f), new ColorRgba(148, 163, 184, 195), black, 1, 1f);
            }

            if (!item.IsEmpty && s != this.DraggedSlot)
            {
                byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                if (upg > 0)
                {
                    ColorRgba pc = upg >= 6 ? new ColorRgba(34, 211, 238, 255) : new ColorRgba(250, 204, 21, 255);
                    spriteBatch.DrawOutlinedString(font, $"+{upg}", new Vector2(rect.Right - 18f, rect.Y + 4f), pc, black, 1, 1f);
                }

                if (item.Quantity > 1)
                {
                    string qTxt = $"x{item.Quantity}";
                    TextMeasureResult qSize = font.MeasureString(qTxt, TextLayoutOptions.Default, 1.5f);
                    spriteBatch.DrawOutlinedString(font, qTxt, new Vector2(rect.Right - qSize.Width - 4f, rect.Bottom - 18f), ColorRgba.White, black, 1, 1.5f);
                }
            }
        }
    }

    public void DrawTopOverlayGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos,
        int hoveredSlot)
    {
        if (this.DraggedSlot >= 0 && mouseScreenPos.HasValue && art is not null)
        {
            Span<ItemSlot> dragSpan = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
            world.CopyInventorySlots(dragSpan);
            if (this.DraggedSlot < dragSpan.Length)
            {
                ItemSlot dragged = dragSpan[this.DraggedSlot];
                if (!dragged.IsEmpty && art.TryGetItem(dragged.ItemId, out CompiledItemVectorArt? dragArt))
                {
                    VectorRigRenderer.DrawItemIcon(batch, dragArt, mouseScreenPos.Value, 28f);
                }
            }
        }

        if (hoveredSlot >= 0 && this.DraggedSlot < 0 && mouseScreenPos.HasValue)
        {
            Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
            world.CopyInventorySlots(slots);
            if (hoveredSlot < slots.Length && !slots[hoveredSlot].IsEmpty)
            {
                float tx = Math.Clamp(mouseScreenPos.Value.X + 16f, 12f, viewportWidth - 274f);
                float ty = Math.Clamp(mouseScreenPos.Value.Y - 94f, 12f, viewportHeight - 102f);
                batch.FillRectangle(new RectangleF(tx, ty, 260f, 88f), new ColorRgba(8, 12, 24, 248));
                batch.DrawRectangle(new RectangleF(tx, ty, 260f, 88f), new ColorRgba(250, 204, 21, 235), 1.5f);
            }
        }
    }

    public void DrawTopOverlayText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos,
        int hoveredSlot)
    {
        if (hoveredSlot < 0 || this.DraggedSlot >= 0 || !mouseScreenPos.HasValue)
        {
            return;
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        if (hoveredSlot >= slots.Length || slots[hoveredSlot].IsEmpty)
        {
            return;
        }

        ItemSlot hItem = slots[hoveredSlot];
        ColorRgba black = new(8, 10, 16, 255);
        float tx = Math.Clamp(mouseScreenPos.Value.X + 16f, 12f, viewportWidth - 274f);
        float ty = Math.Clamp(mouseScreenPos.Value.Y - 94f, 12f, viewportHeight - 102f);

        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in hItem);
        string title = upg > 0 ? $"{StarterGameCatalog.GetDisplayName(hItem.ItemId)} +{upg}" : StarterGameCatalog.GetDisplayName(hItem.ItemId);
        spriteBatch.DrawOutlinedString(font, title, new Vector2(tx + 8f, ty + 8f), new ColorRgba(250, 204, 21, 255), black, 1, 1.5f);

        float upgMult = ItemEnhancementPolicy.GetStatMultiplier(upg);
        string statLine = StarterGameCatalog.Items.TryGet(hItem.ItemId, out ItemArchetype? def)
            ? (def.Category == ItemCategory.Armor
                ? $"DEF +{(int)MathF.Round(def.ArmorDefense * upgMult)} | HP +{(int)MathF.Round(def.BonusHealth * upgMult)} | MP +{(int)MathF.Round(def.BonusMana * upgMult)}"
                : def.Category == ItemCategory.Weapon
                    ? $"DMG {(int)MathF.Round(def.BaseDamage * upgMult)} | RNG {(int)def.AttackRange} | MP {(int)def.AttackManaCost}"
                    : $"Qty {hItem.Quantity} | Max {def.MaxStackSize}")
            : $"Qty {hItem.Quantity}";
        spriteBatch.DrawOutlinedString(font, statLine, new Vector2(tx + 8f, ty + 29f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, StarterGameCatalog.GetActionHint(hItem.ItemId), new Vector2(tx + 8f, ty + 47f), new ColorRgba(226, 232, 240, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, "Click/Drag: Move | RMB/Shift: Quick-Equip", new Vector2(tx + 8f, ty + 66f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
    }

    private static (float Ox, float Oy) ComputeOrigin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelW) * 0.5f), MathF.Floor((viewportHeight - PanelH) * 0.5f));

    private static RectangleF GetCloseButtonRect(float ox, float oy) => new(ox + PanelW - 34f, oy + 10f, 24f, 24f);

    private static RectangleF GetUseEquipButtonRect(float ox, float oy) => new(ox + 492f, oy + 164f, 178f, 28f);

    private static RectangleF GetDropButtonRect(float ox, float oy) => new(ox + 492f, oy + 198f, 102f, 28f);

    private static RectangleF GetDropAllButtonRect(float ox, float oy) => new(ox + 600f, oy + 198f, 70f, 28f);

    private static RectangleF GetSlotRect(float ox, float oy, int slotIndex)
    {
        if (slotIndex >= PlayerInventoryLayout.BagLimit)
        {
            int armorIdx = slotIndex - PlayerInventoryLayout.BagLimit;
            return new RectangleF(ox + 34f, oy + 50f + (armorIdx * 48f), 44f, 44f);
        }

        if (slotIndex < 9)
        {
            float gridStartX = ox + 124f;
            return new RectangleF(gridStartX + (slotIndex * (SlotSize + SlotGap)), oy + 414f, SlotSize, SlotSize);
        }

        int bagIdx = slotIndex - 9;
        int col = bagIdx % 9;
        int row = bagIdx / 9;
        float bx = ox + 124f;
        float by = oy + 256f;
        return new RectangleF(bx + (col * (SlotSize + SlotGap)), by + (row * (SlotSize + SlotGap)), SlotSize, SlotSize);
    }
}

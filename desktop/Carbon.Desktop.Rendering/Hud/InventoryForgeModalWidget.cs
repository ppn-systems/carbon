// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
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
/// Centered 40-slot Minecraft-style Inventory, Armor Paper-Doll (<c>36..39</c>), Live Character Preview,
/// Forge Enhancement Anvil (<c>+0..+10</c>), Floating Comparison Tooltip, and Drag-and-Drop modal (SRP).
/// </summary>
internal sealed class InventoryForgeModalWidget
{
    private const float PanelW = 684f;
    private const float PanelH = 472f;
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

    public bool TryHitTestForgeButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return GetForgeButtonRect(ox, oy).Contains(mousePos);
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

        if (this.SelectedSlot >= 0 &&
            this.SelectedSlot < slots.Length &&
            !slots[this.SelectedSlot].IsEmpty)
        {
            return this.SelectedSlot;
        }

        if (world.SelectedHotbarSlot < slots.Length && !slots[world.SelectedHotbarSlot].IsEmpty)
        {
            return world.SelectedHotbarSlot;
        }

        return -1;
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

        // Dim scrim + beveled obsidian modal frame
        batch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), new ColorRgba(4, 8, 16, 140));
        batch.FillRectangle(new RectangleF(ox, oy, PanelW, PanelH), new ColorRgba(10, 15, 28, 248));
        batch.DrawRectangle(new RectangleF(ox, oy, PanelW, PanelH), new ColorRgba(250, 204, 21, 225), 2f);
        batch.FillRectangle(new RectangleF(ox + 2f, oy + 2f, PanelW - 4f, 30f), new ColorRgba(15, 23, 42, 250));

        // Left pedestal card (Armor slots + Animated Character Preview)
        RectangleF paperCard = new(ox + 16f, oy + 38f, 236f, 198f);
        batch.FillRectangle(paperCard, new ColorRgba(14, 22, 40, 242));
        batch.DrawRectangle(paperCard, new ColorRgba(51, 65, 85, 225), 1.3f);

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

            Vector2 pedPos = new(ox + 158f, oy + 186f);
            batch.FillCircle(pedPos + new Vector2(0f, -6f), 26f, new ColorRgba(4, 8, 16, 125), 16);
            batch.DrawCircle(pedPos + new Vector2(0f, -6f), 27f, new ColorRgba(56, 189, 248, 165), 18, 1.5f);
            VectorRigRenderer.DrawEntityRig(
                batch,
                heroRig,
                pedPos,
                Direction8.South,
                "idle",
                animTime,
                isNormalized: false,
                targetHeightOverride: 76f,
                hitFlash: 0f,
                equippedItem: weaponArt,
                action: default,
                legacySwingTimer: 0f,
                appearancePreset: world.LocalPlayerAppearancePreset,
                equipment: loadout,
                art: art);
        }

        // Right Forge Enhancement Anvil card with forge result pulse border
        RectangleF forgeCard = new(ox + 262f, oy + 38f, 406f, 198f);
        batch.FillRectangle(forgeCard, new ColorRgba(14, 22, 40, 242));
        ColorRgba forgeCardBorder = world.ForgeFeedbackTimer > 0f
            ? (world.LastForgeSucceeded ? new ColorRgba(34, 197, 94, 255) : new ColorRgba(239, 68, 68, 255))
            : new ColorRgba(56, 189, 248, 190);
        batch.DrawRectangle(forgeCard, forgeCardBorder, world.ForgeFeedbackTimer > 0f ? 2.4f : 1.3f);

        int forgeTargetSlot = this.ResolveActionTargetSlot(world);
        ItemSlot forgeItem = forgeTargetSlot >= 0 && forgeTargetSlot < slots.Length ? slots[forgeTargetSlot] : ItemSlot.Empty;
        RectangleF targetBox = new(ox + 278f, oy + 68f, 54f, 54f);
        batch.FillRectangle(targetBox, new ColorRgba(18, 28, 50, 248));
        batch.DrawRectangle(targetBox, forgeItem.IsEmpty ? new ColorRgba(71, 85, 105, 220) : new ColorRgba(250, 204, 21, 240), 1.8f);
        if (!forgeItem.IsEmpty && art is not null && art.TryGetItem(forgeItem.ItemId, out CompiledItemVectorArt? forgeArt))
        {
            VectorRigRenderer.DrawItemIcon(batch, forgeArt, new Vector2(targetBox.X + 27f, targetBox.Y + 27f), 34f);
        }

        bool canForge = !forgeItem.IsEmpty &&
            ItemEnhancementPolicy.CanUpgrade(in forgeItem, out uint m1, out int c1, out uint m2, out int c2, out _) &&
            CountItemInSpan(slots, m1) >= c1 &&
            (m2 == 0U || CountItemInSpan(slots, m2) >= c2);

        RectangleF forgeBtn = GetForgeButtonRect(ox, oy);
        bool hoverForge = mouseScreenPos.HasValue && forgeBtn.Contains(mouseScreenPos.Value);
        batch.FillRectangle(forgeBtn, canForge ? (hoverForge ? new ColorRgba(16, 185, 129, 255) : new ColorRgba(5, 150, 105, 245)) : new ColorRgba(30, 41, 59, 220));
        batch.DrawRectangle(forgeBtn, canForge ? new ColorRgba(110, 231, 183, 255) : new ColorRgba(71, 85, 105, 220), 1.6f);

        RectangleF dropBtn = GetDropButtonRect(ox, oy);
        bool canDrop = !forgeItem.IsEmpty;
        bool hoverDrop = mouseScreenPos.HasValue && dropBtn.Contains(mouseScreenPos.Value);
        batch.FillRectangle(dropBtn, canDrop ? (hoverDrop ? new ColorRgba(220, 38, 38, 255) : new ColorRgba(153, 27, 27, 240)) : new ColorRgba(30, 41, 59, 220));
        batch.DrawRectangle(dropBtn, canDrop ? new ColorRgba(248, 113, 113, 255) : new ColorRgba(71, 85, 105, 220), 1.6f);

        RectangleF dropAllBtn = GetDropAllButtonRect(ox, oy);
        bool hoverDropAll = mouseScreenPos.HasValue && dropAllBtn.Contains(mouseScreenPos.Value);
        batch.FillRectangle(dropAllBtn, canDrop ? (hoverDropAll ? new ColorRgba(185, 28, 28, 255) : new ColorRgba(127, 29, 29, 240)) : new ColorRgba(30, 41, 59, 220));
        batch.DrawRectangle(dropAllBtn, canDrop ? new ColorRgba(248, 113, 113, 255) : new ColorRgba(71, 85, 105, 220), 1.6f);

        // Draw all 40 slots (0..8 hotbar, 9..35 backpack, 36..39 armor)
        int hoveredSlot = -1;
        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity && s < slots.Length; s++)
        {
            RectangleF rect = GetSlotRect(ox, oy, s);
            bool hovered = mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value);
            if (hovered)
            {
                hoveredSlot = s;
            }

            bool selected = s == this.SelectedSlot;
            bool isArmor = s >= PlayerInventoryLayout.BagLimit;
            ColorRgba bg = selected
                ? new ColorRgba(45, 58, 92, 250)
                : (hovered ? new ColorRgba(26, 38, 62, 245) : (isArmor ? new ColorRgba(18, 28, 48, 240) : new ColorRgba(14, 21, 36, 238)));
            batch.FillRectangle(rect, bg);

            ItemSlot item = slots[s];
            CompiledItemVectorArt? itemArt = null;
            ColorRgba border = selected
                ? new ColorRgba(250, 204, 21, 255)
                : (hovered ? ColorRgba.White : (isArmor ? new ColorRgba(148, 163, 184, 210) : new ColorRgba(60, 74, 96, 220)));

            if (!item.IsEmpty && art is not null && art.TryGetItem(item.ItemId, out itemArt) && !selected && !hovered)
            {
                border = new ColorRgba(itemArt.AccentColor.R, itemArt.AccentColor.G, itemArt.AccentColor.B, 185);
            }

            batch.DrawRectangle(rect, border, selected ? 2.2f : 1.3f);
            if (!item.IsEmpty)
            {
                if (itemArt is not null && s != this.DraggedSlot)
                {
                    VectorRigRenderer.DrawItemIcon(batch, itemArt, new Vector2(rect.X + (SlotSize * 0.5f), rect.Y + (SlotSize * 0.46f)), 28f);
                }

                if (StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? def) && def.MaxDurability > 0f)
                {
                    byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                    float effMax = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(def, upg));
                    float frac = Math.Clamp(item.Durability / effMax, 0f, 1f);
                    ColorRgba dCol = frac <= 0f
                        ? new ColorRgba(239, 68, 68, 255)
                        : (frac > 0.5f ? new ColorRgba(56, 189, 248, 240) : new ColorRgba(250, 204, 21, 245));
                    batch.FillRectangle(new RectangleF(rect.X + 4f, rect.Y + SlotSize - 6f, SlotSize - 8f, 3f), new ColorRgba(8, 12, 20, 235));
                    batch.FillRectangle(new RectangleF(rect.X + 4f, rect.Y + SlotSize - 6f, MathF.Max(1f, (SlotSize - 8f) * frac), 3f), dCol);
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
        int viewportHeight,
        int hoveredSlot)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        ColorRgba black = new(8, 10, 16, 255);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        spriteBatch.DrawOutlinedString(font, "INVENTORY / ARMOR / FORGE", new Vector2(ox + 16f, oy + 9f), new ColorRgba(250, 204, 21, 255), black, 1, 2f);
        spriteBatch.DrawOutlinedString(font, "Drag/Click: Move  Shift/RMB: Quick Equip  [X]: Drop  [ESC]: Close", new Vector2(ox + 286f, oy + 11f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);

        spriteBatch.DrawOutlinedString(font, $"LV.{world.LocalPlayerLevel}  DEF {(int)MathF.Round(world.LocalPlayerDefense)}", new Vector2(ox + 118f, oy + 46f), ColorRgba.White, black, 1, 1.5f);
        spriteBatch.DrawOutlinedString(font, $"HP {(int)MathF.Ceiling(world.LocalPlayerHealth)}/{(int)MathF.Ceiling(world.LocalPlayerMaxHealth)}", new Vector2(ox + 102f, oy + 196f), new ColorRgba(252, 165, 165, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"MP {(int)MathF.Floor(world.LocalPlayerMana)}/{(int)MathF.Ceiling(world.LocalPlayerMaxMana)}", new Vector2(ox + 102f, oy + 212f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);

        for (int a = 0; a < s_armorLabels.Length; a++)
        {
            int slotIdx = PlayerInventoryLayout.HeadSlot + a;
            if (slotIdx < slots.Length && slots[slotIdx].IsEmpty)
            {
                RectangleF r = GetSlotRect(ox, oy, slotIdx);
                spriteBatch.DrawOutlinedString(font, s_armorLabels[a], new Vector2(r.X + 4f, r.Y + 16f), new ColorRgba(148, 163, 184, 200), black, 1, 1f);
            }
        }

        // Forge Panel text with concrete before -> after deltas
        spriteBatch.DrawOutlinedString(font, "ENHANCEMENT ANVIL (+1..+10)", new Vector2(ox + 278f, oy + 46f), new ColorRgba(125, 211, 252, 255), black, 1, 1.5f);
        int forgeTargetSlot = this.ResolveActionTargetSlot(world);
        ItemSlot forgeItem = forgeTargetSlot >= 0 && forgeTargetSlot < slots.Length ? slots[forgeTargetSlot] : ItemSlot.Empty;
        if (forgeItem.IsEmpty)
        {
            spriteBatch.DrawOutlinedString(font, "Click gear below to Forge or Drop", new Vector2(ox + 344f, oy + 86f), new ColorRgba(148, 163, 184, 255), black, 1, 1.5f);
        }
        else
        {
            byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in forgeItem);
            string itemName = StarterGameCatalog.GetDisplayName(forgeItem.ItemId);
            spriteBatch.DrawOutlinedString(font, $"{itemName} (+{upg})", new Vector2(ox + 344f, oy + 68f), ColorRgba.White, black, 1, 1.5f);

            if (ItemEnhancementPolicy.CanUpgrade(in forgeItem, out uint m1, out int c1, out uint m2, out int c2, out float chance))
            {
                string deltaText = FormatUpgradeStatDelta(forgeItem.ItemId, upg, (byte)(upg + 1));
                spriteBatch.DrawOutlinedString(font, $"Next +{upg + 1} ({(int)(chance * 100f)}%): {deltaText}", new Vector2(ox + 344f, oy + 88f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
                string req1 = $"{StarterGameCatalog.GetDisplayName(m1)}: {CountItemInSpan(slots, m1)}/{c1}";
                string req2 = m2 != 0U ? $"  |  {StarterGameCatalog.GetDisplayName(m2)}: {CountItemInSpan(slots, m2)}/{c2}" : string.Empty;
                spriteBatch.DrawOutlinedString(font, req1 + req2, new Vector2(ox + 278f, oy + 138f), new ColorRgba(203, 213, 225, 255), black, 1, 1f);
            }
            else
            {
                string maxMsg = upg >= ItemEnhancementPolicy.MaxUpgradeLevel
                    ? "MAX ENHANCEMENT REACHED (+10)"
                    : "Material / Consumable (Not Forgable)";
                spriteBatch.DrawOutlinedString(font, maxMsg, new Vector2(ox + 344f, oy + 92f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
            }
        }

        if (world.ForgeFeedbackTimer > 0f)
        {
            string fbMsg = world.LastForgeSucceeded ? "FORGE SUCCESS!" : "FORGE FAILED!";
            ColorRgba fbCol = world.LastForgeSucceeded ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(248, 113, 113, 255);
            spriteBatch.DrawOutlinedString(font, fbMsg, new Vector2(ox + 278f, oy + 162f), fbCol, black, 1, 1.5f);
        }

        RectangleF forgeBtn = GetForgeButtonRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, "FORGE +1", new Vector2(forgeBtn.X + 26f, forgeBtn.Y + 10f), ColorRgba.White, black, 1, 1.5f);
        RectangleF dropBtn = GetDropButtonRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, "DROP 1", new Vector2(dropBtn.X + 14f, dropBtn.Y + 10f), ColorRgba.White, black, 1, 1f);
        RectangleF dropAllBtn = GetDropAllButtonRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, "ALL", new Vector2(dropAllBtn.X + 16f, dropAllBtn.Y + 10f), ColorRgba.White, black, 1, 1f);

        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity && s < slots.Length; s++)
        {
            ItemSlot item = slots[s];
            if (item.IsEmpty || s == this.DraggedSlot)
            {
                continue;
            }

            RectangleF rect = GetSlotRect(ox, oy, s);
            byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
            if (upg > 0)
            {
                ColorRgba pCol = upg >= 6 ? new ColorRgba(34, 211, 238, 255) : new ColorRgba(250, 204, 21, 255);
                spriteBatch.DrawOutlinedString(font, $"+{upg}", new Vector2(rect.X + SlotSize - 17f, rect.Y + 3f), pCol, black, 1, 1f);
            }

            if (item.Quantity > 1)
            {
                string qty = $"x{item.Quantity}";
                TextMeasureResult qSize = font.MeasureString(qty, TextLayoutOptions.Default, 1.5f);
                spriteBatch.DrawOutlinedString(font, qty, new Vector2(MathF.Floor(rect.X + SlotSize - qSize.Width - 3f), rect.Y + SlotSize - 17f), ColorRgba.White, black, 1, 1.5f);
            }
        }

        if (hoveredSlot >= 0 && hoveredSlot < slots.Length)
        {
            ItemSlot hItem = slots[hoveredSlot];
            if (!hItem.IsEmpty)
            {
                byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in hItem);
                string hLabel = upg > 0 ? $"{StarterGameCatalog.GetDisplayName(hItem.ItemId)} +{upg}" : StarterGameCatalog.GetDisplayName(hItem.ItemId);
                string hint = StarterGameCatalog.GetActionHint(hItem.ItemId);
                spriteBatch.DrawOutlinedString(font, $"{hLabel} - {hint}", new Vector2(ox + 18f, oy + PanelH - 18f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
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
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        if (this.DraggedSlot >= 0 && this.DraggedSlot < slots.Length && mouseScreenPos.HasValue)
        {
            ItemSlot dragged = slots[this.DraggedSlot];
            if (!dragged.IsEmpty && art is not null && art.TryGetItem(dragged.ItemId, out CompiledItemVectorArt? dragArt))
            {
                VectorRigRenderer.DrawItemIcon(batch, dragArt, mouseScreenPos.Value, 30f);
            }
        }

        if (this.DraggedSlot < 0 && hoveredSlot >= 0 && hoveredSlot < slots.Length && !slots[hoveredSlot].IsEmpty && mouseScreenPos.HasValue)
        {
            const float tipW = 296f;
            const float tipH = 92f;
            float tx = Math.Clamp(mouseScreenPos.Value.X + 16f, 10f, viewportWidth - tipW - 10f);
            float ty = Math.Clamp(mouseScreenPos.Value.Y - tipH - 10f, 10f, viewportHeight - tipH - 10f);

            ColorRgba accent = art is not null && art.TryGetItem(slots[hoveredSlot].ItemId, out CompiledItemVectorArt? tipArt)
                ? tipArt.AccentColor
                : new ColorRgba(148, 163, 184, 255);
            batch.FillRectangle(new RectangleF(tx, ty, tipW, tipH), new ColorRgba(8, 12, 24, 248));
            batch.DrawRectangle(new RectangleF(tx, ty, tipW, tipH), accent, 1.5f);
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
        if (this.DraggedSlot >= 0 || hoveredSlot < 0 || !mouseScreenPos.HasValue)
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
        const float tipW = 296f;
        const float tipH = 92f;
        float tx = Math.Clamp(mouseScreenPos.Value.X + 16f, 10f, viewportWidth - tipW - 10f);
        float ty = Math.Clamp(mouseScreenPos.Value.Y - tipH - 10f, 10f, viewportHeight - tipH - 10f);
        ColorRgba black = new(8, 10, 16, 255);

        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in hItem);
        string title = upg > 0 ? $"{StarterGameCatalog.GetDisplayName(hItem.ItemId)} +{upg}" : StarterGameCatalog.GetDisplayName(hItem.ItemId);
        spriteBatch.DrawOutlinedString(font, title, new Vector2(tx + 10f, ty + 8f), new ColorRgba(250, 204, 21, 255), black, 1, 1.5f);

        float mult = ItemEnhancementPolicy.GetStatMultiplier(upg);
        float defVal = StarterGameCatalog.GetArmorDefense(hItem.ItemId) * mult;
        float hpVal = StarterGameCatalog.GetArmorBonusHealth(hItem.ItemId) * mult;
        float dmgVal = StarterGameCatalog.GetWeaponBaseDamage(hItem.ItemId) * mult;

        string statLine = defVal > 0f
            ? $"DEF +{defVal:F1} | +{hpVal:F0} HP | +{StarterGameCatalog.GetArmorBonusMana(hItem.ItemId) * mult:F0} MP"
            : $"DMG {dmgVal:F1} | Flora {StarterGameCatalog.GetFloraHarvestDamage(hItem.ItemId) * mult:F0} | Mine {StarterGameCatalog.GetMineralHarvestDamage(hItem.ItemId) * mult:F0}";
        spriteBatch.DrawOutlinedString(font, statLine, new Vector2(tx + 10f, ty + 30f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);

        // Equipped comparison when hovering an unequipped armor piece
        if (hoveredSlot < PlayerInventoryLayout.BagLimit &&
            StarterGameCatalog.Items.TryGet(hItem.ItemId, out ItemArchetype? hDef) &&
            hDef.EquipSlot != EquipmentSlotKind.None)
        {
            int eqIdx = PlayerInventoryLayout.GetArmorSlotIndex(hDef.EquipSlot);
            ItemSlot equipped = eqIdx >= 0 && eqIdx < slots.Length ? slots[eqIdx] : ItemSlot.Empty;
            float eqMult = equipped.IsEmpty ? 0f : ItemEnhancementPolicy.GetStatMultiplier(ItemEnhancementPolicy.GetUpgradeLevel(in equipped));
            float eqDef = equipped.IsEmpty ? 0f : StarterGameCatalog.GetArmorDefense(equipped.ItemId) * eqMult;
            float eqHp = equipped.IsEmpty ? 0f : StarterGameCatalog.GetArmorBonusHealth(equipped.ItemId) * eqMult;
            float dDef = defVal - eqDef;
            float dHp = hpVal - eqHp;
            string cmp = $"vs Eq: {(dDef >= 0f ? "+" : string.Empty)}{dDef:F1} DEF, {(dHp >= 0f ? "+" : string.Empty)}{dHp:F0} HP";
            ColorRgba cmpCol = dDef >= 0f ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(248, 113, 113, 255);
            spriteBatch.DrawOutlinedString(font, cmp, new Vector2(tx + 10f, ty + 47f), cmpCol, black, 1, 1f);
        }

        if (StarterGameCatalog.Items.TryGet(hItem.ItemId, out ItemArchetype? durDef) && durDef.MaxDurability > 0f)
        {
            float effMax = ItemEnhancementPolicy.GetEffectiveMaxDurability(durDef, upg);
            spriteBatch.DrawOutlinedString(font, $"DUR {(int)MathF.Ceiling(hItem.Durability)}/{(int)effMax}", new Vector2(tx + 10f, ty + 66f), ColorRgba.White, black, 1, 1f);
        }
        else
        {
            spriteBatch.DrawOutlinedString(font, StarterGameCatalog.GetActionHint(hItem.ItemId), new Vector2(tx + 10f, ty + 66f), new ColorRgba(203, 213, 225, 255), black, 1, 1f);
        }
    }

    private static string FormatUpgradeStatDelta(uint itemId, byte curUpg, byte nextUpg)
    {
        float curMult = ItemEnhancementPolicy.GetStatMultiplier(curUpg);
        float nextMult = ItemEnhancementPolicy.GetStatMultiplier(nextUpg);
        float defCur = StarterGameCatalog.GetArmorDefense(itemId) * curMult;
        if (defCur > 0f)
        {
            float defNext = StarterGameCatalog.GetArmorDefense(itemId) * nextMult;
            return $"DEF {defCur:F1} -> {defNext:F1}";
        }

        float dmgCur = StarterGameCatalog.GetWeaponBaseDamage(itemId) * curMult;
        float dmgNext = StarterGameCatalog.GetWeaponBaseDamage(itemId) * nextMult;
        return $"DMG {dmgCur:F1} -> {dmgNext:F1}";
    }

    private static int CountItemInSpan(ReadOnlySpan<ItemSlot> slots, uint itemId)
    {
        if (itemId == 0U)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].ItemId == itemId)
            {
                total += slots[i].Quantity;
            }
        }

        return total;
    }

    private static (float Ox, float Oy) ComputeOrigin(int viewportWidth, int viewportHeight)
    {
        float ox = MathF.Floor((viewportWidth - PanelW) * 0.5f);
        float oy = MathF.Max(14f, MathF.Floor(((viewportHeight - PanelH) * 0.5f) - 16f));
        return (ox, oy);
    }

    private static RectangleF GetDropButtonRect(float ox, float oy) => new(ox + 382f, oy + 190f, 68f, 34f);

    private static RectangleF GetDropAllButtonRect(float ox, float oy) => new(ox + 454f, oy + 190f, 52f, 34f);

    private static RectangleF GetForgeButtonRect(float ox, float oy) => new(ox + 514f, oy + 190f, 140f, 34f);

    private static RectangleF GetSlotRect(float ox, float oy, int slotIndex)
    {
        if (slotIndex >= PlayerInventoryLayout.HeadSlot && slotIndex <= PlayerInventoryLayout.FeetSlot)
        {
            int row = slotIndex - PlayerInventoryLayout.HeadSlot;
            return new RectangleF(ox + 26f, oy + 44f + (row * 47f), SlotSize, SlotSize);
        }

        float gridX = ox + 114f;
        if (slotIndex >= PlayerInventoryLayout.BackpackStart && slotIndex < PlayerInventoryLayout.BagLimit)
        {
            int idx = slotIndex - PlayerInventoryLayout.BackpackStart;
            int col = idx % 9;
            int row = idx / 9;
            return new RectangleF(gridX + (col * (SlotSize + SlotGap)), oy + 244f + (row * (SlotSize + SlotGap)), SlotSize, SlotSize);
        }

        int hCol = Math.Clamp(slotIndex, 0, 8);
        return new RectangleF(gridX + (hCol * (SlotSize + SlotGap)), oy + 402f, SlotSize, SlotSize);
    }
}

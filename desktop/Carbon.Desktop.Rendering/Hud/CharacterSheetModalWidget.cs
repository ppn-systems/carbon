// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Character tab (<c>[Y]</c>) of the <see cref="PlayerModalTabs"/> workspace (ADR-021). Left card: portrait, level and
/// experience, and every derived stat. Right card: the five attributes with their per-point effects and <c>+</c>
/// buttons and the unspent point count. Hovering a <c>+</c> button previews, in green,
/// each stat that point would change. All numbers come from the synced <c>progression.json</c>.
/// </summary>
internal sealed class CharacterSheetModalWidget
{
    private static readonly ColorRgba s_previewText = new(120, 226, 150, 255);

    private readonly List<SkillDefinition> _skillScratch = new(8);
    private readonly CharacterStatsPreview _preview = new();

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

    public bool TryHitTestStatPlusButton(Vector2 mousePos, int viewportWidth, int viewportHeight, out PrimaryAttributeId attribute)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        for (int i = 0; i < CharacterStatsPreview.Attributes.Length; i++)
        {
            if (GetStatPlusRect(in layout, i).Contains(mousePos))
            {
                attribute = CharacterStatsPreview.Attributes[i];
                return true;
            }
        }

        attribute = PrimaryAttributeId.Vitality;
        return false;
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
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        PlayerModalTabs.DrawWindowChromeSkin(spriteBatch, skin, in layout, PlayerModalTab.Character, hasSkills, mouseScreenPos);

        RectangleF leftCard = GetLeftCardRect(in layout);
        RectangleF rightCard = GetRightCardRect(in layout);
        skin.DrawCard(spriteBatch, in leftCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 236);
        skin.DrawCard(spriteBatch, in rightCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        PrimaryAttributeId? hovered = null;
        bool hasAp = world.LocalUnspentStatPoints > 0;
        for (int i = 0; i < CharacterStatsPreview.Attributes.Length; i++)
        {
            RectangleF rowBox = GetStatRowRect(in layout, i);
            RectangleF plusRect = GetStatPlusRect(in layout, i);
            bool hoverPlus = mouseScreenPos.HasValue && plusRect.Contains(mouseScreenPos.Value);
            if (hoverPlus)
            {
                hovered = CharacterStatsPreview.Attributes[i];
            }

            skin.DrawInset(spriteBatch, in rowBox, hoverPlus && hasAp ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);
            skin.DrawButton(
                spriteBatch,
                in plusRect,
                hasAp ? (hoverPlus ? new ColorRgba(44, 114, 82, 245) : CarbonUiPalette.ConfirmFill) : CarbonUiPalette.SlotFill,
                hasAp ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderSubtle);
        }

        RectangleF statsBox = GetStatsRect(in layout);
        skin.DrawInset(spriteBatch, in statsBox, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);

        // The model is refreshed once per frame here (the skin pass runs first), so the text pass reads a consistent preview.
        _ = _preview.Update(world, SyncedProgressionConfig.Current, hovered);
    }

    public void DrawGeometry(
        GeometryBatch batch,
        IWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        float animTime)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;
        RectangleF leftCard = GetLeftCardRect(in layout);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        if (art is not null && art.TryGetCharacter(world.LocalPlayerAppearancePreset, out CompiledVectorRig? rig))
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

            float pedR = MathF.Round(28f * s);
            Vector2 pedPos = new(leftCard.X + MathF.Round(56f * s), leftCard.Y + MathF.Round(84f * s));
            batch.FillCircle(pedPos + new Vector2(0f, -4f * s), pedR, new ColorRgba(6, 9, 14, 145), 16);
            batch.DrawCircle(pedPos + new Vector2(0f, -4f * s), pedR + 1f, CarbonUiPalette.BorderSubtle, 16, 1.3f);
            VectorRigRenderer.DrawEntityRig(
                batch,
                rig,
                pedPos,
                Direction8.South,
                "idle",
                animTime,
                isNormalized: false,
                targetHeightOverride: MathF.Round(84f * s),
                hitFlash: 0f,
                equippedItem: weaponArt,
                action: default,
                swingTimer: 0f,
                appearancePreset: world.LocalPlayerAppearancePreset,
                equipment: loadout,
                art: art);
        }

        // XP meter under the name.
        RectangleF xpTrack = GetXpTrackRect(in layout);
        float xpRatio = Math.Clamp(world.LocalPlayerExperience / MathF.Max(1f, world.LocalPlayerXpToNext), 0f, 1f);
        batch.FillRectangle(xpTrack, CarbonUiPalette.SlotFill);
        if (xpRatio > 0.004f)
        {
            batch.FillRectangle(new RectangleF(xpTrack.X, xpTrack.Y, xpTrack.Width * xpRatio, xpTrack.Height), CarbonUiPalette.XpFill);
        }

        batch.DrawRectangle(xpTrack, CarbonUiPalette.BorderSubtle, 1f);
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        int ptx,
        int pty,
        float fps,
        bool showTelemetry)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;
        float ts = layout.TextScale;
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        ushort unspentAp = world.LocalUnspentStatPoints;
        PlayerModalTabs.DrawWindowChromeText(
            spriteBatch,
            font,
            in layout,
            PlayerModalTab.Character,
            hasSkills,
            GameText.Get("character.title"),
            GameText.Format("character.points_badge", new TextArg("count", unspentAp)),
            unspentAp > 0);

        RectangleF leftCard = GetLeftCardRect(in layout);
        RectangleF rightCard = GetRightCardRect(in layout);
        ColorRgba black = CarbonUiPalette.TextOutline;

        // Identity, next to the portrait.
        float idX = leftCard.X + MathF.Round(120f * s);
        float idW = leftCard.Right - idX - MathF.Round(12f * s);
        string heroName = string.IsNullOrWhiteSpace(world.ActiveCharacterName) ? GameText.Get("character.hero") : world.ActiveCharacterName;
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, heroName, idW, layout.TitleScale), new Vector2(idX, leftCard.Y + MathF.Round(22f * s)), CarbonUiPalette.TextPrimary, black, 1, layout.TitleScale);
        spriteBatch.DrawOutlinedString(font, GameText.Format("character.class_level", new TextArg("class", GameText.ClassName(world.LocalPlayerClass)), new TextArg("level", world.LocalPlayerLevel)), new Vector2(idX, leftCard.Y + MathF.Round(52f * s)), CarbonUiPalette.TextWarm, black, 1, ts);
        string vitals = GameText.Format(
            "character.vitals",
            new TextArg("hp", (long)MathF.Ceiling(world.LocalPlayerHealth)),
            new TextArg("max_hp", (long)MathF.Ceiling(world.LocalPlayerMaxHealth)),
            new TextArg("mp", (long)MathF.Floor(world.LocalPlayerMana)),
            new TextArg("max_mp", (long)MathF.Ceiling(world.LocalPlayerMaxMana)));
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, vitals, idW, ts), new Vector2(idX, leftCard.Y + MathF.Round(76f * s)), CarbonUiPalette.TextMuted, black, 1, ts);
        RectangleF xpTrack = GetXpTrackRect(in layout);
        spriteBatch.DrawOutlinedString(font, GameText.Format("character.xp", new TextArg("xp", world.LocalPlayerExperience.ToString("N0", CultureInfo.InvariantCulture)), new TextArg("next", world.LocalPlayerXpToNext.ToString("N0", CultureInfo.InvariantCulture))), new Vector2(xpTrack.X, xpTrack.Bottom + MathF.Round(6f * s)), CarbonUiPalette.TextMuted, black, 1, ts);

        this.DrawDerivedStatsText(spriteBatch, font, in layout);

        if (showTelemetry)
        {
            string telemetry = GameText.Format(
                "character.telemetry",
                new TextArg("x", ptx),
                new TextArg("y", pty),
                new TextArg("fps", (long)fps),
                new TextArg("net", world.TransportLabel));
            spriteBatch.DrawOutlinedString(font, telemetry, new Vector2(leftCard.X + MathF.Round(12f * s), leftCard.Bottom - MathF.Round(22f * s)), CarbonUiPalette.TextMuted, black, 1, ts);
        }

        // Attributes card.
        float pad = MathF.Round(18f * s);
        spriteBatch.DrawOutlinedString(font, GameText.Get("character.attributes"), new Vector2(rightCard.X + pad, rightCard.Y + MathF.Round(14f * s)), CarbonUiPalette.TextWarm, black, 1, layout.TitleScale);
        string pointsText = unspentAp > 0 ? GameText.Format("character.to_spend", new TextArg("count", unspentAp)) : GameText.Get("character.no_points");
        float pointsW = font.MeasureString(pointsText, scale: ts).Width;
        spriteBatch.DrawOutlinedString(font, pointsText, new Vector2(rightCard.Right - pad - pointsW, rightCard.Y + MathF.Round(18f * s)), unspentAp > 0 ? s_previewText : CarbonUiPalette.TextMuted, black, 1, ts);

        for (int i = 0; i < CharacterStatsPreview.Attributes.Length; i++)
        {
            PrimaryAttributeId attr = CharacterStatsPreview.Attributes[i];
            RectangleF rowRect = GetStatRowRect(in layout, i);
            RectangleF plusRect = GetStatPlusRect(in layout, i);
            float textW = plusRect.X - rowRect.X - MathF.Round(20f * s);
            bool previewing = _preview.PreviewAttribute == attr;
            spriteBatch.DrawOutlinedString(
                font,
                $"{CharacterStatsPreview.NameOf(attr)}  {world.GetAttributePoints(attr)}{(previewing ? " > " + (world.GetAttributePoints(attr) + 1).ToString(CultureInfo.InvariantCulture) : string.Empty)}",
                new Vector2(rowRect.X + MathF.Round(12f * s), rowRect.Y + MathF.Round(8f * s)),
                previewing ? s_previewText : CarbonUiPalette.TextPrimary,
                black,
                1,
                ts);
            spriteBatch.DrawOutlinedString(
                font,
                CarbonUiSkin.TruncateToWidth(font, _preview.DescribePerPoint(attr), textW, ts),
                new Vector2(rowRect.X + MathF.Round(12f * s), rowRect.Y + MathF.Round(32f * s)),
                CarbonUiPalette.TextMuted,
                black,
                1,
                ts);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "+", in plusRect, unspentAp > 0 ? CarbonUiPalette.TextPrimary : CarbonUiPalette.TextMuted, black, layout.TitleScale);
        }

        RectangleF hint = GetHintRect(in layout);
        spriteBatch.DrawOutlinedString(
            font,
            CarbonUiSkin.TruncateToWidth(font, GameText.Get("character.hint"), hint.Width, ts),
            new Vector2(hint.X, hint.Y),
            CarbonUiPalette.TextMuted,
            black,
            1,
            ts);
    }

    private static RectangleF GetLeftCardRect(in PlayerModalLayout layout)
    {
        float leftW = MathF.Round(308f * layout.Scale);
        return new RectangleF(layout.ContentX, layout.ContentY, leftW, layout.ContentH);
    }

    private static RectangleF GetRightCardRect(in PlayerModalLayout layout)
    {
        float leftW = MathF.Round(308f * layout.Scale);
        float gap = MathF.Round(14f * layout.Scale);
        return new RectangleF(layout.ContentX + leftW + gap, layout.ContentY, layout.ContentW - leftW - gap, layout.ContentH);
    }

    private static RectangleF GetXpTrackRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF left = GetLeftCardRect(in layout);
        float pad = MathF.Round(14f * s);
        return new RectangleF(left.X + pad, left.Y + MathF.Round(136f * s), left.Width - (pad * 2f), MathF.Max(8f, MathF.Round(9f * s)));
    }

    private static RectangleF GetStatsRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF left = GetLeftCardRect(in layout);
        float pad = MathF.Round(10f * s);
        float top = left.Y + MathF.Round(176f * s);
        float bottomReserve = MathF.Round(30f * s); // room for the telemetry line under the box
        return new RectangleF(left.X + pad, top, left.Width - (pad * 2f), left.Bottom - top - bottomReserve);
    }

    private static RectangleF GetStatRowRect(in PlayerModalLayout layout, int rowIndex)
    {
        float s = layout.Scale;
        RectangleF rightCard = GetRightCardRect(in layout);
        float pad = MathF.Round(14f * s);
        return new RectangleF(
            rightCard.X + pad,
            rightCard.Y + MathF.Round(48f * s) + (rowIndex * MathF.Round(70f * s)),
            rightCard.Width - (pad * 2f),
            MathF.Round(62f * s));
    }

    private static RectangleF GetStatPlusRect(in PlayerModalLayout layout, int rowIndex)
    {
        float s = layout.Scale;
        RectangleF row = GetStatRowRect(in layout, rowIndex);
        float btn = MathF.Round(38f * s);
        return new RectangleF(row.Right - btn - MathF.Round(12f * s), row.Y + MathF.Round((62f - 38f) * 0.5f * s), btn, btn);
    }

    /// <summary>The one-line allocation hint under the last attribute row.</summary>
    private static RectangleF GetHintRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF lastRow = GetStatRowRect(in layout, CharacterStatsPreview.Attributes.Length - 1);
        return new RectangleF(lastRow.X, lastRow.Bottom + MathF.Round(14f * s), lastRow.Width, MathF.Round(20f * s));
    }

    /// <summary>Draws the derived-stats card: two columns of label + value; previewed changes show the next value in green.</summary>
    private void DrawDerivedStatsText(SpriteBatch spriteBatch, BitmapFont font, in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        float ts = layout.TextScale;
        ColorRgba black = CarbonUiPalette.TextOutline;
        RectangleF statsBox = GetStatsRect(in layout);
        spriteBatch.DrawOutlinedString(font, GameText.Get("character.stats"), new Vector2(statsBox.X + MathF.Round(10f * s), statsBox.Y + MathF.Round(8f * s)), CarbonUiPalette.TextWarm, black, 1, ts);
        if (_preview.Config is null)
        {
            spriteBatch.DrawOutlinedString(font, GameText.Get("character.syncing"), new Vector2(statsBox.X + MathF.Round(10f * s), statsBox.Y + MathF.Round(36f * s)), CarbonUiPalette.TextMuted, black, 1, ts);
        }
        else
        {
            DerivedCharacterStats now = _preview.Current;
            DerivedCharacterStats next = _preview.Preview;
            int perColumn = (CharacterStatsPreview.DisplayRowCount + 1) / 2;
            float colW = (statsBox.Width - MathF.Round(20f * s)) * 0.5f;
            float rowH = MathF.Round(32f * s);
            for (int i = 0; i < CharacterStatsPreview.DisplayRowCount; i++)
            {
                int col = i / perColumn;
                int row = i % perColumn;
                float x = statsBox.X + MathF.Round(10f * s) + (col * colW);
                float y = statsBox.Y + MathF.Round(34f * s) + (row * rowH);
                bool changes = _preview.PreviewChanges(i);
                string value = changes ? _preview.FormatStat(i, in next) : _preview.FormatStat(i, in now);
                float valueW = font.MeasureString(value, scale: ts).Width;
                float valueRight = x + colW - MathF.Round(12f * s);

                // Labels give way to values (translations differ in width).
                float labelW = MathF.Max(0f, valueRight - valueW - x - MathF.Round(6f * s));
                spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, CharacterStatsPreview.StatLabel(i), labelW, ts), new Vector2(x, y), CarbonUiPalette.TextMuted, black, 1, ts);
                spriteBatch.DrawOutlinedString(font, value, new Vector2(valueRight - valueW, y), changes ? s_previewText : CarbonUiPalette.TextPrimary, black, 1, ts);
            }
        }
    }
}

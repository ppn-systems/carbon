// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Top-center RPG Monster/NPC Target Frame skinned cleanly with <see cref="CarbonUiSkin"/>, showing a live
/// vector portrait, threat rank, current vs. max HP readout, smooth ghost-damage trail, and attack telegraph glow (SRP).
/// </summary>
internal sealed class MonsterTargetWidget
{
    private readonly CachedText _title = new();
    private RemoteEntityView _activeTarget;
    private float _ghostRatio;
    private bool _hasTarget;
    private bool _isLocked;

    public void UpdateTarget(IWorldReadModel world, CombatFeedbackTracker? feedback, Vector2? aimedWorldPos)
    {
        if (feedback is not null)
        {
            _hasTarget = feedback.TryResolveTargetEntity(world, aimedWorldPos, out _activeTarget, out _ghostRatio);
        }
        else
        {
            _hasTarget = false;
        }

        _isLocked = _hasTarget && _activeTarget.NetworkId != 0UL && _activeTarget.NetworkId == world.LockedTargetNetworkId;
    }

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin, int viewportWidth)
    {
        if (!_hasTarget)
        {
            return;
        }

        const float cardW = 340f;
        const float cardH = 74f;
        float x = MathF.Floor((viewportWidth - cardW) * 0.5f);
        const float y = 14f;
        ColorRgba frameBorder = _activeTarget.AttackAnimRemaining > 0f
            ? CarbonUiPalette.HpFill
            : CarbonUiPalette.BorderOuter;
        skin.DrawPanel(spriteBatch, UiPanelStyle.OrnateHighlight, new RectangleF(x, y, cardW, cardH), CarbonUiPalette.WindowFill, frameBorder);
        skin.DrawSlot(spriteBatch, new RectangleF(x + 10f, y + 11f, 52f, 52f), CarbonUiPalette.SlotFill, frameBorder);
    }

    public void DrawGeometry(GeometryBatch batch, IVectorArtRepository? art, int viewportWidth, float animTime)
    {
        if (!_hasTarget)
        {
            return;
        }

        const float cardW = 340f;
        float x = MathF.Floor((viewportWidth - cardW) * 0.5f);
        const float y = 14f;

        RectangleF pBox = new(x + 10f, y + 11f, 52f, 52f);
        CompiledVectorRig? rig = ResolveRig(art, in _activeTarget);
        if (rig is not null)
        {
            VectorRigRenderer.DrawPortraitRig(
                batch,
                rig,
                new Vector2(pBox.X + 26f, pBox.Y + 26f),
                boxSize: 44f,
                Direction8.South,
                _activeTarget.AttackAnimRemaining > 0f ? "attack" : "idle",
                animTime,
                art: art);
        }

        float barX = x + 70f;
        float barY = y + 40f;
        const float barW = 256f;
        const float barH = 20f;
        float maxHp = MathF.Max(1f, _activeTarget.MaxHealth);
        float ratio = Math.Clamp(_activeTarget.CurrentHealth / maxHp, 0f, 1f);
        float ghost = Math.Clamp(_ghostRatio, ratio, 1f);

        batch.FillRectangle(new RectangleF(barX - 1f, barY - 1f, barW + 2f, barH + 2f), new ColorRgba(8, 10, 14, 245));
        batch.FillRectangle(new RectangleF(barX, barY, barW, barH), CarbonUiPalette.SlotFill);
        if (ghost > ratio + 0.004f)
        {
            batch.FillRectangle(new RectangleF(barX, barY, barW * ghost, barH), CarbonUiPalette.BorderActive);
        }

        if (ratio > 0.004f)
        {
            ColorRgba fill = _activeTarget.Kind == EntityKind.Npc
                ? CarbonUiPalette.XpFill
                : CarbonUiPalette.HpFill;
            batch.FillRectangle(new RectangleF(barX, barY, barW * ratio, barH), fill);
            batch.FillRectangle(new RectangleF(barX, barY, barW * ratio, 3f), new ColorRgba(255, 255, 255, 55));
        }

        for (int seg = 1; seg <= 3; seg++)
        {
            float tx = MathF.Floor(barX + (barW * (seg * 0.25f)));
            batch.FillRectangle(new RectangleF(tx, barY, 1f, barH), new ColorRgba(8, 10, 14, 145));
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, IVectorArtRepository? art, int viewportWidth)
    {
        if (!_hasTarget)
        {
            return;
        }

        const float cardW = 340f;
        float x = MathF.Floor((viewportWidth - cardW) * 0.5f);
        const float y = 14f;
        ColorRgba black = CarbonUiPalette.TextOutline;

        CompiledVectorRig? rig = ResolveRig(art, in _activeTarget);
        ushort displayLv = _activeTarget.Level > 0 ? _activeTarget.Level : (rig is not null ? rig.Level : (ushort)1);
        string subtitle = _isLocked
            ? GameText.Get("hud.target.locked")
            : (rig is not null && !string.IsNullOrWhiteSpace(rig.Subtitle)
                ? rig.Subtitle
                : GameText.Get(_activeTarget.Kind == EntityKind.Npc ? "hud.target.npc" : "hud.target.hostile"));

        ColorRgba nameColor = _activeTarget.Kind == EntityKind.Npc
            ? CarbonUiPalette.TextWarm
            : CarbonUiPalette.TextPrimary;

        string safeSub = CarbonUiSkin.TruncateToWidth(font, subtitle, 92f, 1f);
        TextMeasureResult subSize = font.MeasureString(safeSub, TextLayoutOptions.Default, 1f);
        float maxNameWidth = MathF.Max(80f, cardW - 72f - subSize.Width - 20f);
        string fullTitle = CarbonUiSkin.TruncateToWidth(
            font,
            displayLv > 0 ? _title.Format("hud.target.title_level", new TextArg("level", displayLv), new TextArg("name", _activeTarget.Name)) : _activeTarget.Name,
            maxNameWidth,
            1f);

        spriteBatch.DrawOutlinedString(font, fullTitle, new Vector2(x + 70f, y + 13f), nameColor, black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, safeSub, new Vector2(x + cardW - subSize.Width - 14f, y + 13f), CarbonUiPalette.TextMuted, black, 1, 1f);

        int curHp = (int)MathF.Ceiling(_activeTarget.CurrentHealth);
        int maxHp = (int)MathF.Ceiling(MathF.Max(1f, _activeTarget.MaxHealth));
        string hpText = $"{curHp} / {maxHp}";
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, hpText, new RectangleF(x + 70f, y + 40f, 256f, 20f), CarbonUiPalette.TextPrimary, black, 1f);
    }

    private static CompiledVectorRig? ResolveRig(IVectorArtRepository? art, in RemoteEntityView target)
    {
        if (art is null)
        {
            return null;
        }

        if (target.Kind == EntityKind.Monster && art.TryGetMonster(target.ArchetypeId, out CompiledVectorRig? mr))
        {
            return mr;
        }

        if (target.Kind == EntityKind.Npc && art.TryGetNpc(target.ArchetypeId, out CompiledVectorRig? nr))
        {
            return nr;
        }

        if (target.Kind == EntityKind.Player && art.TryGetCharacter((byte)target.ArchetypeId, out CompiledVectorRig? cr))
        {
            return cr;
        }

        return null;
    }
}

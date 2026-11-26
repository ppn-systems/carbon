// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions;
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
    private RemoteEntityView _activeTarget;
    private float _ghostRatio;
    private bool _hasTarget;
    private bool _isLocked;

    public void UpdateTarget(IClientWorldReadModel world, CombatFeedbackTracker? feedback, Vector2? aimedWorldPos)
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

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin, int viewportWidth, float animTime)
    {
        if (!_hasTarget)
        {
            return;
        }

        const float cardW = 340f;
        const float cardH = 72f;
        float x = MathF.Floor((viewportWidth - cardW) * 0.5f);
        const float y = 14f;
        ColorRgba frameBorder = _activeTarget.AttackAnimRemaining > 0f
            ? new ColorRgba(248, 113, 113, (byte)(190 + (65 * MathF.Abs(MathF.Sin(animTime * 16f)))))
            : (_activeTarget.Kind == EntityKind.Npc ? new ColorRgba(234, 179, 8, 235) : new ColorRgba(225, 29, 72, 235));
        skin.DrawPanel(spriteBatch, UiPanelStyle.OrnateHighlight, new RectangleF(x, y, cardW, cardH), new ColorRgba(10, 15, 28, 236), frameBorder);
        skin.DrawSlot(spriteBatch, new RectangleF(x + 10f, y + 10f, 52f, 52f), new ColorRgba(12, 18, 34, 244), frameBorder);
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

        RectangleF pBox = new(x + 10f, y + 10f, 52f, 52f);
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
        float barY = y + 38f;
        const float barW = 256f;
        const float barH = 18f;
        float maxHp = MathF.Max(1f, _activeTarget.MaxHealth);
        float ratio = Math.Clamp(_activeTarget.CurrentHealth / maxHp, 0f, 1f);
        float ghost = Math.Clamp(_ghostRatio, ratio, 1f);

        batch.FillRectangle(new RectangleF(barX - 1f, barY - 1f, barW + 2f, barH + 2f), new ColorRgba(6, 9, 16, 245));
        batch.FillRectangle(new RectangleF(barX, barY, barW, barH), new ColorRgba(26, 36, 54, 240));
        if (ghost > ratio + 0.004f)
        {
            batch.FillRectangle(new RectangleF(barX, barY, barW * ghost, barH), new ColorRgba(253, 224, 71, 225));
        }

        if (ratio > 0.004f)
        {
            ColorRgba fill = _activeTarget.Kind == EntityKind.Npc
                ? new ColorRgba(234, 179, 8, 255)
                : new ColorRgba(225, 29, 72, 255);
            batch.FillRectangle(new RectangleF(barX, barY, barW * ratio, barH), fill);
            batch.FillRectangle(new RectangleF(barX, barY, barW * ratio, 4f), new ColorRgba(255, 255, 255, 75));
        }

        for (int seg = 1; seg <= 3; seg++)
        {
            float tx = MathF.Floor(barX + (barW * (seg * 0.25f)));
            batch.FillRectangle(new RectangleF(tx, barY, 1f, barH), new ColorRgba(8, 12, 20, 145));
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
        ColorRgba black = new(10, 12, 18, 255);

        CompiledVectorRig? rig = ResolveRig(art, in _activeTarget);
        ushort displayLv = _activeTarget.Level > 0 ? _activeTarget.Level : (rig is not null ? rig.Level : (ushort)1);
        string lockTag = _isLocked ? " [LOCK]" : string.Empty;
        string levelPrefix = displayLv > 0 ? $"[LV.{displayLv}] " : string.Empty;
        string subtitle = _isLocked
            ? "LOCKED [TAB]"
            : (rig is not null && !string.IsNullOrWhiteSpace(rig.Subtitle)
                ? rig.Subtitle
                : (_activeTarget.Kind == EntityKind.Npc ? "TOWN NPC" : "HOSTILE"));

        ColorRgba nameColor = _activeTarget.Kind == EntityKind.Npc
            ? new ColorRgba(253, 224, 71, 255)
            : new ColorRgba(254, 202, 202, 255);

        spriteBatch.DrawOutlinedString(font, $"{levelPrefix}{_activeTarget.Name}{lockTag}", new Vector2(x + 70f, y + 12f), nameColor, black, 1, 1.5f);
        TextMeasureResult subSize = font.MeasureString(subtitle, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, subtitle, new Vector2(x + cardW - subSize.Width - 14f, y + 14f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);

        int curHp = (int)MathF.Ceiling(_activeTarget.CurrentHealth);
        int maxHp = (int)MathF.Ceiling(MathF.Max(1f, _activeTarget.MaxHealth));
        int pct = (int)MathF.Round(Math.Clamp(_activeTarget.CurrentHealth / MathF.Max(1f, _activeTarget.MaxHealth), 0f, 1f) * 100f);
        string hpText = $"{curHp} / {maxHp} ({pct}%)";
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, hpText, new RectangleF(x + 70f, y + 38f, 256f, 18f), ColorRgba.White, black, 1.5f);
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

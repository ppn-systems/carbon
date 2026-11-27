// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Numerics;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Capture;
using Carbon.Desktop.Rendering.Effects;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Desktop.Rendering.World;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.Windowing.Abstractions;
using Silk.NET.OpenGL;

namespace Carbon.Desktop.Capture;

/// <summary>
/// Impact filmstrip (<c>hero_impact.bmp</c> from <c>--showcase</c>): pairs of rows for the same swing at a slime, a whiff
/// and a confirmed hit that arrived during the wind-up (as on LAN), sampled at equal real-time steps. The hit row runs
/// through the real <see cref="ImpactPresenter"/>: the target flashes only when the blade reaches it, and the hero's pose
/// holds (hit-stop) and then catches up so both rows end on the same frame (docs/art/combat-animation.md §3.4).
/// </summary>
internal static class HeroImpactFilmstrip
{
    private const float Zoom = 2f;
    private const float LabelWidth = 150f;
    private const float ColWidth = 125f;
    private const float FirstRowY = 60f;
    private const float RowHeight = 108f;
    private const int Columns = 9;
    private const float FrameDt = 1f / 120f;

    // Wild Slime's monster rig id (catalog.cvg.json).
    private const ushort SlimeRigId = 101;

    // (label, action kind, held item id, hit, critical): item ids are the shipped catalogue (items.json): 3 Iron Sword,
    // 1 Wood Axe, 0 bare hands.
    private static readonly (string Label, ActionAnimationKind Kind, uint ItemId, bool Hit, bool Crit)[] s_rows =
    [
        ("Sword, whiff", ActionAnimationKind.SwordSlash, 3U, false, false),
        ("Sword, hit", ActionAnimationKind.SwordSlash, 3U, true, false),
        ("Axe, whiff", ActionAnimationKind.AxeChop, 1U, false, false),
        ("Axe, crit", ActionAnimationKind.AxeChop, 1U, true, true),
        ("Fists, hit", ActionAnimationKind.Punch, 0U, true, false),
    ];

    /// <summary>Renders the impact filmstrip page and captures it to <paramref name="path"/>.</summary>
    /// <param name="window">Showcase window.</param>
    /// <param name="geom">Geometry batch.</param>
    /// <param name="sprites">Sprite batch for labels.</param>
    /// <param name="font">Label font.</param>
    /// <param name="art">Vector art library.</param>
    /// <param name="ortho">Screen projection.</param>
    /// <param name="path">Output BMP path.</param>
    public static void Render(IGameWindow window, GeometryBatch geom, SpriteBatch sprites, BitmapFont font, CarbonVectorArtLibrary art, Matrix4x4 ortho, string path)
    {
        if (!art.TryGetCharacter(0, out CompiledVectorRig? hero) || !art.TryGetMonster(SlimeRigId, out CompiledVectorRig? slime))
        {
            return;
        }

        window.Device.SetClearColor(new Vector4(0.07f, 0.09f, 0.12f, 1.0f));
        window.Device.Clear(ClearBufferMask.ColorBufferBit);
        Span<float> shownAt = stackalloc float[Columns];
        Span<float> flashAt = stackalloc float[Columns];
        Span<bool> frozenAt = stackalloc bool[Columns];

        geom.Begin(Matrix4x4.CreateScale(Zoom, Zoom, 1f) * ortho);
        for (int row = 0; row < s_rows.Length; row++)
        {
            (_, ActionAnimationKind kind, uint itemId, bool hit, bool crit) = s_rows[row];
            Simulate(kind, hit, crit, shownAt, flashAt, frozenAt);
            CompiledItemVectorArt? item = null;
            if (itemId != 0U)
            {
                _ = art.TryGetItem(itemId, out item);
            }

            HeroAttackTiming timing = HeroAttackTiming.For(HeroAttackTiming.StyleOf(kind, 0));
            for (int col = 0; col < Columns; col++)
            {
                float cellX = LabelWidth + (col * ColWidth);
                float cellY = FirstRowY + (row * RowHeight);
                geom.FillRectangle(new RectangleF((cellX + 3f) / Zoom, (cellY + 3f) / Zoom, (ColWidth - 6f) / Zoom, (RowHeight - 6f) / Zoom), new ColorRgba(20, 26, 38, 255));

                Vector2 heroFoot = new((cellX + (ColWidth * 0.30f)) / Zoom, (cellY + (RowHeight * 0.82f)) / Zoom);
                Vector2 slimeFoot = heroFoot + new Vector2(21f, 0f);
                ActionState shown = new(kind, timing.Total, timing.Total * (1f - MathF.Min(shownAt[col], 0.9999f)), Vector2.UnitX, itemId, 0);
                geom.FillCircle(slimeFoot + new Vector2(1f, 1f), 8f, new ColorRgba(0, 0, 0, 80), 12);
                VectorRigRenderer.DrawEntityRig(geom, slime, slimeFoot, Direction8.West, "idle", 0.3f, isNormalized: false, hitFlash: flashAt[col] * 0.9f);
                VectorRigRenderer.DrawEntityRig(
                    geom,
                    hero,
                    heroFoot + WorldSceneRenderer.AttackLean(in shown),
                    Direction8.East,
                    WorldSceneRenderer.ResolveCharacterClip(shown, 0f, moving: false),
                    shown.Progress,
                    isNormalized: true,
                    equippedItem: item,
                    action: shown);
                WeaponTrailRenderer.Draw(geom, hero, item, heroFoot, Direction8.East, in shown, 0);
                if (frozenAt[col])
                {
                    geom.DrawRectangle(new RectangleF((cellX + 3f) / Zoom, (cellY + 3f) / Zoom, (ColWidth - 6f) / Zoom, (RowHeight - 6f) / Zoom), new ColorRgba(251, 113, 133, 255), 0.8f);
                }
            }
        }

        geom.End();

        sprites.Begin(DepthSortMode.Immediate, transformMatrix: ortho);
        ColorRgba black = new(0, 0, 0, 255);
        ColorRgba gold = new(250, 204, 21, 255);
        ColorRgba white = new(241, 245, 249, 255);
        ColorRgba cyan = new(125, 211, 252, 255);
        sprites.DrawOutlinedString(font, "HERO IMPACT: HIT CONFIRMED DURING WIND-UP, PRESENTED AT CONTACT (RED = HIT-STOP)", new Vector2(16f, 14f), gold, black, 1, 1.0f);
        for (int row = 0; row < s_rows.Length; row++)
        {
            (string label, ActionAnimationKind kind, _, bool hit, bool crit) = s_rows[row];
            Simulate(kind, hit, crit, shownAt, flashAt, frozenAt);
            HeroAttackTiming timing = HeroAttackTiming.For(HeroAttackTiming.StyleOf(kind, 0));
            float y = FirstRowY + (row * RowHeight) + 30f;
            sprites.DrawOutlinedString(font, label, new Vector2(12f, y), white, black, 1, 0.9f);
            for (int col = 0; col < Columns; col++)
            {
                float ms = col * timing.Total / (Columns - 1) * 1000f;
                string text = string.Create(CultureInfo.InvariantCulture, $"{ms:0}ms clip {shownAt[col]:0.00}");
                sprites.DrawOutlinedString(font, text, new Vector2(LabelWidth + (col * ColWidth) + 8f, FirstRowY + (row * RowHeight) + 6f), frozenAt[col] ? new ColorRgba(251, 113, 133, 255) : cyan, black, 1, 0.62f);
            }
        }

        sprites.End();
        ScreenshotHelper.Capture(window.Device.Gl, window.Width, window.Height, path);
    }

    // Steps the real presenter at 120 Hz through one swing and records, at each column's real time, the displayed clip
    // time, the slime's flash and whether the hit-stop holds the pose.
    private static void Simulate(ActionAnimationKind kind, bool hit, bool crit, Span<float> shownAt, Span<float> flashAt, Span<bool> frozenAt)
    {
        HeroAttackTiming timing = HeroAttackTiming.For(HeroAttackTiming.StyleOf(kind, 0));
        ImpactPresenter presenter = new();
        StrikeImpactEvent[] hits = hit ? [new StrikeImpactEvent(1UL, 2UL, Vector2.Zero, crit ? 40f : 12f, 50f, crit, false, true, false)] : [];
        int col = 0;
        int steps = (int)MathF.Ceiling(timing.Total / FrameDt);
        for (int frame = 0; frame <= steps && col < Columns; frame++)
        {
            float t = frame * FrameDt;
            ActionState session = new(kind, timing.Total, MathF.Max(1e-4f, timing.Total - t), Vector2.UnitX, 0U, 0);
            presenter.Step(session, Vector2.UnitX, frame == 1 ? hits : [], FrameDt, ImpactFeelOptions.Default);
            float columnTime = col * timing.Total / (Columns - 1);
            if (t + (FrameDt * 0.5f) >= columnTime)
            {
                shownAt[col] = WorldSceneRenderer.ShownAction(session, presenter).Progress;
                flashAt[col] = presenter.FlashFor(2UL);
                frozenAt[col] = presenter.IsFrozen;
                col++;
            }
        }

        for (; col < Columns; col++)
        {
            shownAt[col] = 1f;
            flashAt[col] = 0f;
            frozenAt[col] = false;
        }
    }
}

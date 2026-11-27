// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Globalization;
using System.Numerics;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Capture;
using Carbon.Desktop.Rendering.Effects;
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
/// Hero attack filmstrip (<c>hero_attacks.bmp</c> from <c>--showcase</c>, <c>tools/screenshots/filmstrip.sh</c>): one row per
/// attack (bare hands, sword combo, axe, pickaxe, bow, staff), sampled left to right through the swing at world scale (x2),
/// through the same clip mapping, rig renderer and swing VFX as the world, so a before/after pair shows exactly what
/// players see (<c>docs/art/combat-animation.md</c>).
/// </summary>
internal static class HeroAttackFilmstrip
{
    private const float Zoom = 2f;
    private const float LabelWidth = 150f;
    private const float ColWidth = 125f;
    private const float FirstRowY = 60f;
    private const float RowHeight = 73f;
    private const float FootRadius = 7f;

    // (label, action kind, held item id, combo step): item ids are the shipped catalogue (items.json). Bare hands
    // render as a punch once the hero has one (ActionAnimationKind.Punch); before that they played the sword clip.
    private static readonly (string Label, ActionAnimationKind Kind, uint ItemId, byte Combo, byte Upgrade)[] s_rows =
    [
        ("Bare hands 1", ActionAnimationKind.Punch, 0U, 0, 0),
        ("Bare hands 2", ActionAnimationKind.Punch, 0U, 1, 0),
        ("Iron Sword 1", ActionAnimationKind.SwordSlash, 3U, 0, 0),
        ("Iron Sword 2 +3", ActionAnimationKind.SwordSlash, 3U, 1, 3),
        ("Iron Sword 3 +7", ActionAnimationKind.SwordSlash, 3U, 2, 7),
        ("Wood Axe", ActionAnimationKind.AxeChop, 1U, 0, 0),
        ("Stone Pickaxe", ActionAnimationKind.PickaxeMine, 2U, 0, 0),
        ("Hunting Bow", ActionAnimationKind.BowShot, 24U, 0, 0),
        ("Arcane Staff", ActionAnimationKind.StaffCast, 25U, 0, 0),
    ];

    private static readonly float[] s_samples = [0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.65f, 0.8f, 0.95f];

    /// <summary>Renders the filmstrip page and captures it to <paramref name="path"/>.</summary>
    /// <param name="window">Showcase window.</param>
    /// <param name="geom">Geometry batch.</param>
    /// <param name="sprites">Sprite batch for labels.</param>
    /// <param name="font">Label font.</param>
    /// <param name="art">Vector art library.</param>
    /// <param name="ortho">Screen projection.</param>
    /// <param name="path">Output BMP path.</param>
    public static void Render(
        IGameWindow window,
        GeometryBatch geom,
        SpriteBatch sprites,
        BitmapFont font,
        CarbonVectorArtLibrary art,
        Matrix4x4 ortho,
        string path)
    {
        if (!art.TryGetCharacter(0, out CompiledVectorRig? rig))
        {
            return;
        }

        window.Device.SetClearColor(new Vector4(0.07f, 0.09f, 0.12f, 1.0f));
        window.Device.Clear(ClearBufferMask.ColorBufferBit);

        // World units are drawn at Zoom x so the 35-unit hero reads; cells and labels are laid out in screen pixels.
        geom.Begin(Matrix4x4.CreateScale(Zoom, Zoom, 1f) * ortho);
        for (int row = 0; row < s_rows.Length; row++)
        {
            (_, ActionAnimationKind kind, uint itemId, byte combo, byte upgrade) = s_rows[row];
            CompiledItemVectorArt? item = null;
            if (itemId != 0U)
            {
                _ = art.TryGetItem(itemId, out item);
            }

            for (int col = 0; col < s_samples.Length; col++)
            {
                float cellX = LabelWidth + (col * ColWidth);
                float cellY = FirstRowY + (row * RowHeight);
                geom.FillRectangle(new RectangleF((cellX + 3f) / Zoom, (cellY + 3f) / Zoom, (ColWidth - 6f) / Zoom, (RowHeight - 6f) / Zoom), new ColorRgba(20, 26, 38, 255));

                Vector2 entityPos = new((cellX + (ColWidth * 0.42f)) / Zoom, ((cellY + (RowHeight * 0.80f)) / Zoom) - FootRadius);
                Vector2 footPos = entityPos + new Vector2(0f, FootRadius);
                float progress = s_samples[col];
                const float Duration = 1f;
                ActionState action = new(kind, Duration, Duration * (1f - progress), Vector2.UnitX, itemId, combo);
                if (progress <= 0f)
                {
                    // Progress 0 is the first frame of the swing, not "no action".
                    action = action with { RemainingSeconds = Duration * 0.999f };
                }

                geom.FillCircle(new Vector2(footPos.X + 1.5f, footPos.Y + 1.5f), FootRadius * 1.18f, new ColorRgba(0, 0, 0, 88), 12);
                if (WorldSceneRenderer.ReleaseVfxProgress(action.Kind, action.ComboStep, action.Progress) is float vfx)
                {
                    ActionVfxRenderer.DrawActionTrail(geom, entityPos, action.Kind, action.AimDirection, vfx, action.ComboStep, action.EquippedItemId);
                }

                VectorRigRenderer.DrawEntityRig(
                    geom,
                    rig,
                    footPos + WorldSceneRenderer.AttackLean(in action),
                    Direction8.East,
                    WorldSceneRenderer.ResolveCharacterClip(action, 0f, moving: false),
                    action.Progress,
                    isNormalized: true,
                    equippedItem: item,
                    action: action,
                    equipment: new VisualEquipmentLoadout(0, 0, 0, 0, (ushort)itemId, upgrade));
                WeaponTrailRenderer.Draw(geom, rig, item, footPos, Direction8.East, in action, upgrade);
            }
        }

        geom.End();

        sprites.Begin(DepthSortMode.Immediate, transformMatrix: ortho);
        ColorRgba black = new(0, 0, 0, 255);
        ColorRgba gold = new(250, 204, 21, 255);
        ColorRgba white = new(241, 245, 249, 255);
        ColorRgba cyan = new(125, 211, 252, 255);
        sprites.DrawOutlinedString(font, "HERO ATTACK FILMSTRIPS (WORLD SCALE x2, FACING EAST)", new Vector2(16f, 14f), gold, black, 1, 1.2f);
        for (int col = 0; col < s_samples.Length; col++)
        {
            string label = string.Create(CultureInfo.InvariantCulture, $"t = {s_samples[col]:0.00}");
            sprites.DrawOutlinedString(font, label, new Vector2(LabelWidth + (col * ColWidth) + 30f, 42f), cyan, black, 1, 0.85f);
        }

        for (int row = 0; row < s_rows.Length; row++)
        {
            (string label, ActionAnimationKind kind, uint itemId, byte combo, _) = s_rows[row];
            float y = FirstRowY + (row * RowHeight) + 18f;
            ActionState probe = new(kind, 1f, 0.5f, Vector2.UnitX, itemId, combo);
            sprites.DrawOutlinedString(font, label, new Vector2(12f, y), white, black, 1, 0.9f);
            sprites.DrawOutlinedString(font, "clip: " + WorldSceneRenderer.ResolveCharacterClip(probe, 0f, moving: false), new Vector2(12f, y + 20f), gold, black, 1, 0.8f);

            // Phase of each sample from the attack's timing: anticipation, active (strike live) or recovery.
            HeroAttackTiming timing = HeroAttackTiming.For(HeroAttackTiming.StyleOf(kind, combo));
            for (int col = 0; col < s_samples.Length && timing.Total > 0f; col++)
            {
                float t = s_samples[col];
                (string phase, ColorRgba colour) = t < timing.ContactFraction
                    ? ("ANTIC", cyan)
                    : t < timing.ActiveEndFraction ? ("ACTIVE", new ColorRgba(251, 113, 133, 255)) : ("RECOVER", new ColorRgba(148, 163, 184, 255));
                sprites.DrawOutlinedString(font, phase, new Vector2(LabelWidth + (col * ColWidth) + 8f, FirstRowY + (row * RowHeight) + 6f), colour, black, 1, 0.7f);
            }
        }

        sprites.End();
        ScreenshotHelper.Capture(window.Device.Gl, window.Width, window.Height, path);
    }
}

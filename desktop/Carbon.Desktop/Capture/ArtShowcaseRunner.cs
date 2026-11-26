// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Capture;
using Carbon.Desktop.Rendering.Effects;
using Carbon.Desktop.Rendering.Fonts;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Textures;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Configuration;
using Carbon.Graphics.Windowing.Window;
using Carbon.Items.Catalog;
using Carbon.Items.Definitions;
using Silk.NET.OpenGL;

namespace Carbon.Desktop.Capture;

/// <summary>
/// Headless-friendly windowed visual showcase generator (<c>--showcase &lt;outputDir&gt;</c>)
/// that renders upgraded <c>.cvg.json</c> monster rigs, attack animations, VFX trails,
/// items, and structures to BMP files for automated visual inspection.
/// </summary>
internal static class ArtShowcaseRunner
{
    private static readonly (ushort ArchetypeId, string AttackClip, ActionAnimationKind VfxKind)[] s_monsterLineup =
    [
        (WorldActorCatalog.WildSlime, "attack", ActionAnimationKind.MonsterStrike),
        (WorldActorCatalog.FrostWolf, "attack", ActionAnimationKind.MonsterStrike),
        (WorldActorCatalog.SkeletonArcher, "attack_bow", ActionAnimationKind.MonsterBowShot),
        (WorldActorCatalog.RockGolem, "attack_slam", ActionAnimationKind.MonsterHeavySlam),
        (WorldActorCatalog.AshWraith, "attack_cast", ActionAnimationKind.MonsterSpellCast),
        (WorldActorCatalog.OrcShaman, "attack_cast", ActionAnimationKind.MonsterSpellCast),
        (WorldActorCatalog.CrimsonDemon, "attack_slam", ActionAnimationKind.MonsterHeavySlam),
    ];

    public static int Run(string outputDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        _ = Directory.CreateDirectory(outputDir);

        WindowSettings settings = new()
        {
            Title = "Carbon Art Showcase Capture",
            Width = 1280,
            Height = 720,
            VSync = false,
            IsResizable = false,
        };

        using GameWindow window = new(settings);
        int framesRendered = 0;

        window.Render += (w, _) =>
        {
            if (framesRendered == 0)
            {
                RenderAndCapture(w, outputDir);
                framesRendered++;
                w.Close();
            }
        };

        window.Run();
        return framesRendered > 0 ? 0 : 1;
    }

    private static void RenderAndCapture(IGameWindow window, string outputDir)
    {
        int w = window.Width;
        int h = window.Height;
        Matrix4x4 ortho = Matrix4x4.CreateOrthographicOffCenter(0f, w, h, 0f, -1f, 1f);

        CarbonVectorArtLibrary? art = CarbonVectorArtLibrary.LoadOrDefault();
        if (art is null)
        {
            return;
        }

        using GeometryBatch geom = new(window.Device);
        using SpriteBatch sprites = new(window.Device);
        (BitmapFont font, Texture2D whitePixel) = GameFontLoader.Load(window.Device);

        try
        {
            // Sheet 1: Monsters Idle (Top) vs Attack + VFX (Bottom)
            window.Device.SetViewport(0, 0, (uint)w, (uint)h);
            window.Device.SetClearColor(new Vector4(0.08f, 0.10f, 0.14f, 1.0f));
            window.Device.Clear(ClearBufferMask.ColorBufferBit);

            geom.Begin(ortho);
            for (int i = 0; i < s_monsterLineup.Length; i++)
            {
                (ushort archId, string atkClip, ActionAnimationKind vfxKind) = s_monsterLineup[i];
                float cx = 96f + (i * 176f);
                if (!art.TryGetMonster(archId, out CompiledVectorRig? rig))
                {
                    continue;
                }

                // Idle row
                geom.FillCircle(new Vector2(cx, 258f), 26f, new ColorRgba(24, 30, 42, 220), 16);
                VectorRigRenderer.DrawEntityRig(
                    geom,
                    rig,
                    new Vector2(cx, 258f),
                    Direction8.South,
                    "idle",
                    0.25f,
                    isNormalized: false,
                    targetHeightOverride: 108f);

                // Attack row
                geom.FillCircle(new Vector2(cx, 540f), 26f, new ColorRgba(42, 24, 32, 220), 16);
                VectorRigRenderer.DrawEntityRig(
                    geom,
                    rig,
                    new Vector2(cx, 540f),
                    Direction8.East,
                    atkClip,
                    0.55f,
                    isNormalized: true,
                    targetHeightOverride: 108f);
                ActionVfxRenderer.DrawActionTrail(
                    geom,
                    new Vector2(cx + 12f, 505f),
                    vfxKind,
                    Vector2.UnitX,
                    0.52f,
                    comboStep: 0);
            }

            geom.End();

            sprites.Begin(DepthSortMode.Immediate, transformMatrix: ortho);
            ColorRgba black = new(0, 0, 0, 255);
            ColorRgba gold = new(250, 204, 21, 255);
            ColorRgba white = new(241, 245, 249, 255);
            sprites.DrawOutlinedString(font, "CARBON MONSTER RIGS & ATTACK ANIMATION SHOWCASE", new Vector2(28f, 24f), gold, black, 1, 1.4f);
            sprites.DrawOutlinedString(font, "TOP ROW: IDLE SILHOUETTE   |   BOTTOM ROW: ARCHETYPE ATTACK + VFX", new Vector2(28f, 52f), white, black, 1, 1.0f);

            for (int i = 0; i < s_monsterLineup.Length; i++)
            {
                (ushort archId, string atkClip, _) = s_monsterLineup[i];
                MonsterArchetypeSpec spec = WorldActorCatalog.GetMonsterSpec(archId);
                float cx = 96f + (i * 176f);
                sprites.DrawOutlinedString(font, spec.Name, new Vector2(cx - 52f, 294f), white, black, 1, 1.0f);
                sprites.DrawOutlinedString(font, $"Drop: {StarterGameCatalog.GetDisplayName(spec.DropItemId)}", new Vector2(cx - 56f, 316f), gold, black, 1, 0.85f);
                sprites.DrawOutlinedString(font, $"Clip: {atkClip}", new Vector2(cx - 48f, 580f), new ColorRgba(125, 211, 252, 255), black, 1, 0.9f);
            }

            sprites.End();
            ScreenshotHelper.Capture(window.Device.Gl, w, h, Path.Combine(outputDir, "monsters_showcase.bmp"));

            // Sheet 2: All 39 Items Grid
            window.Device.SetClearColor(new Vector4(0.09f, 0.11f, 0.15f, 1.0f));
            window.Device.Clear(ClearBufferMask.ColorBufferBit);

            geom.Begin(ortho);
            ReadOnlySpan<ItemArchetype> allItems = StarterGameCatalog.Items.All;
            const int cols = 8;
            for (int i = 0; i < allItems.Length; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float bx = 76f + (col * 150f);
                float by = 128f + (row * 116f);
                geom.FillRectangle(new RectangleF(bx - 32f, by - 32f, 64f, 64f), new ColorRgba(26, 32, 46, 230));
                geom.DrawRectangle(new RectangleF(bx - 32f, by - 32f, 64f, 64f), new ColorRgba(71, 85, 105, 255), 1.5f);
                if (art.TryGetItem(allItems[i].Id, out CompiledItemVectorArt? itemArt))
                {
                    VectorRigRenderer.DrawItemIcon(geom, itemArt, new Vector2(bx, by), 44f);
                }
            }

            geom.End();

            sprites.Begin(DepthSortMode.Immediate, transformMatrix: ortho);
            sprites.DrawOutlinedString(font, "CARBON ITEM & STRUCTURE KIT CATALOG (39 ITEMS)", new Vector2(28f, 24f), gold, black, 1, 1.4f);
            for (int i = 0; i < allItems.Length; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float bx = 76f + (col * 150f);
                float by = 128f + (row * 116f);
                sprites.DrawOutlinedString(font, allItems[i].DisplayName, new Vector2(bx - 42f, by + 38f), white, black, 1, 0.85f);
            }

            sprites.End();
            ScreenshotHelper.Capture(window.Device.Gl, w, h, Path.Combine(outputDir, "items_showcase.bmp"));
        }
        finally
        {
            font.Dispose();
            whitePixel.Dispose();
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using Carbon.Contracts.Entities.S2C;
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
    // ADR-032 D14 water creatures, from the shore to the abyss: (archetype, attack clip, name, habitat).
    private static readonly (ushort ArchetypeId, string AttackClip, string Name, string Habitat)[] s_waterLineup =
    [
        (WorldActorCatalog.ShoreCrab, "attack", "Shore Crab", "Shore - Medium"),
        (WorldActorCatalog.PiranhaSlime, "attack_leap", "Piranha Slime", "Shallow - Deep"),
        (WorldActorCatalog.DriftJelly, "attack_spell", "Drift Jelly", "Medium - Deep"),
        (WorldActorCatalog.AbyssEel, "attack_leap", "Abyss Eel", "Deep - Abyss"),
        (WorldActorCatalog.AbyssLeviathan, "attack_spell", "Abyss Leviathan", "Boss - Abyss lair"),
    ];

    private static readonly (ushort ArchetypeId, string AttackClip, ActionAnimationKind VfxKind)[] s_monsterLineup =
    [
        (WorldActorCatalog.WildSlime, "attack_leap", ActionAnimationKind.MonsterStrike),
        (WorldActorCatalog.FrostWolf, "attack_leap", ActionAnimationKind.MonsterStrike),
        (WorldActorCatalog.SkeletonArcher, "attack_bow", ActionAnimationKind.MonsterBowShot),
        (WorldActorCatalog.RockGolem, "attack_slam", ActionAnimationKind.MonsterHeavySlam),
        (WorldActorCatalog.AshWraith, "attack_spell", ActionAnimationKind.MonsterSpellCast),
        (WorldActorCatalog.OrcShaman, "attack_cast", ActionAnimationKind.MonsterSpellCast),
        (WorldActorCatalog.CrimsonDemon, "attack_slam", ActionAnimationKind.MonsterHeavySlam),
    ];

    // (archetype, attack kind, shape, area radius, half-angle degrees, label): one filmstrip row per monster attack.
    private static readonly (ushort ArchetypeId, byte Kind, byte Shape, float Radius, float HalfAngleDeg, string Label)[] s_attackLineup =
    [
        (WorldActorCatalog.WildSlime, EntityAttackPhase.KindLeap, 0, 26f, 0f, "Wild Slime - Leap"),
        (WorldActorCatalog.WildSlime, EntityAttackPhase.KindMelee, 1, 22f, 50f, "Wild Slime - Bite"),
        (WorldActorCatalog.FrostWolf, EntityAttackPhase.KindLeap, 0, 22f, 0f, "Frost Wolf - Pounce"),
        (WorldActorCatalog.SkeletonArcher, EntityAttackPhase.KindProjectile, 0, 0f, 0f, "Skeleton - Bow Shot"),
        (WorldActorCatalog.RockGolem, EntityAttackPhase.KindMelee, 1, 34f, 80f, "Rock Golem - Slam"),
        (WorldActorCatalog.AshWraith, EntityAttackPhase.KindSpell, 0, 40f, 0f, "Ash Wraith - Eruption"),
        (WorldActorCatalog.OrcShaman, EntityAttackPhase.KindSpell, 1, 60f, 30f, "Orc Shaman - Hex Cone"),
        (WorldActorCatalog.OrcShaman, EntityAttackPhase.KindProjectile, 0, 0f, 0f, "Orc Shaman - Bolt"),
        (WorldActorCatalog.CrimsonDemon, EntityAttackPhase.KindLeap, 0, 36f, 0f, "Crimson Demon - Leap"),
    ];

    // ADR-032 D15: the abyss boss's three telegraphed patterns.
    private static readonly (ushort ArchetypeId, byte Kind, byte Shape, float Radius, float HalfAngleDeg, string Label)[] s_bossAttackLineup =
    [
        (WorldActorCatalog.AbyssLeviathan, EntityAttackPhase.KindSpell, 0, 72f, 0f, "Leviathan - Tidal Crush"),
        (WorldActorCatalog.AbyssLeviathan, EntityAttackPhase.KindLeap, 0, 46f, 0f, "Leviathan - Abyssal Lunge"),
        (WorldActorCatalog.AbyssLeviathan, EntityAttackPhase.KindMelee, 1, 58f, 120f, "Leviathan - Tail Sweep"),
    ];

    // (phase, progress) samples rendered left to right in each filmstrip row.
    private static readonly (byte Phase, float Progress, string Label)[] s_phaseSamples =
    [
        (EntityAttackPhase.PhaseWindup, 0.05f, "WINDUP 0%"),
        (EntityAttackPhase.PhaseWindup, 0.6f, "WINDUP 60%"),
        (EntityAttackPhase.PhaseWindup, 1f, "WINDUP END"),
        (EntityAttackPhase.PhaseTravel, 0.5f, "TRAVEL 50%"),
        (EntityAttackPhase.PhaseActive, 0.1f, "ACTIVE"),
        (EntityAttackPhase.PhaseActive, 0.9f, "ACTIVE END"),
        (EntityAttackPhase.PhaseRecovery, 0.4f, "RECOVERY"),
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
            ItemRules itemRules = ItemRules.LoadShippedDefaults(); // offline capture tool: no server, so the shipped tables
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
                sprites.DrawOutlinedString(font, $"Drop: {itemRules.GetDisplayName(spec.DropItemId)}", new Vector2(cx - 56f, 316f), gold, black, 1, 0.85f);
                sprites.DrawOutlinedString(font, $"Clip: {atkClip}", new Vector2(cx - 48f, 580f), new ColorRgba(125, 211, 252, 255), black, 1, 0.9f);
            }

            sprites.End();
            ScreenshotHelper.Capture(window.Device.Gl, w, h, Path.Combine(outputDir, "monsters_showcase.bmp"));
            CaptureWaterCreatures(window, art, geom, sprites, font, ortho, outputDir);

            // Sheet 2: All 39 Items Grid
            window.Device.SetClearColor(new Vector4(0.09f, 0.11f, 0.15f, 1.0f));
            window.Device.Clear(ClearBufferMask.ColorBufferBit);

            geom.Begin(ortho);
            ReadOnlySpan<ItemArchetype> allItems = itemRules.Items.All;
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

            RenderAttackPhaseSheet(window, geom, sprites, font, art, ortho, s_attackLineup, "MONSTER ATTACK PHASE TIMELINES (SERVER PHASE -> CLIP TIME, ADR-013)", Path.Combine(outputDir, "monsters_attack_phases.bmp"));
            RenderAttackPhaseSheet(window, geom, sprites, font, art, ortho, s_bossAttackLineup, "ABYSS LEVIATHAN ATTACK PATTERNS AND TELEGRAPHS (ADR-032 D15)", Path.Combine(outputDir, "boss_attack_phases.bmp"));
            RenderDayNightEliteSheet(window, geom, sprites, font, art, ortho, Path.Combine(outputDir, "monsters_day_night_elite.bmp"));
            HeroAttackFilmstrip.Render(window, geom, sprites, font, art, ortho, Path.Combine(outputDir, "hero_attacks.bmp"));
            HeroImpactFilmstrip.Render(window, geom, sprites, font, art, ortho, Path.Combine(outputDir, "hero_impact.bmp"));
            HeroBodySheet.Render(window, geom, sprites, font, art, ortho, Path.Combine(outputDir, "hero_bodies.bmp"));
        }
        finally
        {
            font.Dispose();
            whitePixel.Dispose();
        }
    }

    private static readonly ushort[] s_dayNightLineup =
    [
        WorldActorCatalog.WildSlime,
        WorldActorCatalog.SlimeKing,
        WorldActorCatalog.OrcShaman,
        WorldActorCatalog.OrcWarlord,
        WorldActorCatalog.FrostWolf,
        WorldActorCatalog.SkeletonArcher,
        WorldActorCatalog.RockGolem,
        WorldActorCatalog.AshWraith,
        WorldActorCatalog.CrimsonDemon,
    ];

    private static void RenderDayNightEliteSheet(
        IGameWindow window,
        GeometryBatch geom,
        SpriteBatch sprites,
        BitmapFont font,
        CarbonVectorArtLibrary art,
        Matrix4x4 ortho,
        string path)
    {
        const float FirstX = 90f;
        const float Spacing = 137f;
        const float DayFootY = 300f;
        const float NightFootY = 620f;
        const float Scale = 2.6f; // rigs are drawn at world height x Scale so relative sizes (elites are bigger) stay honest

        window.Device.SetClearColor(new Vector4(0.06f, 0.07f, 0.10f, 1.0f));
        window.Device.Clear(ClearBufferMask.ColorBufferBit);

        geom.Begin(ortho);
        geom.FillRectangle(new RectangleF(16f, 70f, window.Width - 32f, 268f), new ColorRgba(92, 128, 92, 255));
        geom.FillRectangle(new RectangleF(16f, 390f, window.Width - 32f, 268f), new ColorRgba(16, 22, 46, 255));
        for (int i = 0; i < s_dayNightLineup.Length; i++)
        {
            ushort archId = s_dayNightLineup[i];
            if (!art.TryGetMonster(archId, out CompiledVectorRig? rig))
            {
                continue;
            }

            float cx = FirstX + (i * Spacing);
            RigOverlay elite = rig.IsElite ? RigOverlay.EliteCrown : RigOverlay.None;
            float height = rig.WorldHeight * Scale;

            geom.FillCircle(new Vector2(cx, DayFootY), 20f, new ColorRgba(0, 0, 0, 70), 16);
            VectorRigRenderer.DrawEntityRig(geom, rig, new Vector2(cx, DayFootY), Direction8.South, "idle", 0.2f, isNormalized: false,
                targetHeightOverride: height, overlays: elite, overlayTime: 0.4f + i);

            geom.FillCircle(new Vector2(cx, NightFootY), 20f, new ColorRgba(0, 0, 0, 110), 16);
            VectorRigRenderer.DrawEntityRig(geom, rig, new Vector2(cx, NightFootY), Direction8.South, "idle", 0.2f, isNormalized: false,
                targetHeightOverride: height, overlays: elite | RigOverlay.NightGlow, overlayTime: 0.4f + i);
        }

        geom.End();

        sprites.Begin(DepthSortMode.Immediate, transformMatrix: ortho);
        ColorRgba black = new(0, 0, 0, 255);
        ColorRgba gold = new(250, 204, 21, 255);
        ColorRgba white = new(241, 245, 249, 255);
        sprites.DrawOutlinedString(font, "MONSTERS: DAY VS NIGHT, NORMAL VS ELITE (RIG OVERLAYS, NO LIGHTS)", new Vector2(24f, 22f), gold, black, 1, 1.2f);
        sprites.DrawOutlinedString(font, "DAY", new Vector2(28f, 80f), white, black, 1, 1.1f);
        sprites.DrawOutlinedString(font, "NIGHT: EYE GLOW + AURA", new Vector2(28f, 400f), white, black, 1, 1.1f);
        for (int i = 0; i < s_dayNightLineup.Length; i++)
        {
            if (!art.TryGetMonster(s_dayNightLineup[i], out CompiledVectorRig? rig))
            {
                continue;
            }

            float cx = FirstX + (i * Spacing);
            sprites.DrawOutlinedString(font, rig.Title, new Vector2(cx - 50f, DayFootY + 12f), white, black, 1, 0.8f);
            sprites.DrawOutlinedString(font, rig.Title, new Vector2(cx - 50f, NightFootY + 12f), white, black, 1, 0.8f);
            if (rig.IsElite)
            {
                sprites.DrawOutlinedString(font, "ELITE", new Vector2(cx - 18f, DayFootY + 30f), gold, black, 1, 0.8f);
                sprites.DrawOutlinedString(font, "ELITE", new Vector2(cx - 18f, NightFootY + 30f), gold, black, 1, 0.8f);
            }
        }

        sprites.End();
        ScreenshotHelper.Capture(window.Device.Gl, window.Width, window.Height, path);
    }

    private static void RenderAttackPhaseSheet(
        IGameWindow window,
        GeometryBatch geom,
        SpriteBatch sprites,
        BitmapFont font,
        CarbonVectorArtLibrary art,
        Matrix4x4 ortho,
        (ushort ArchetypeId, byte Kind, byte Shape, float Radius, float HalfAngleDeg, string Label)[] lineup,
        string title,
        string path)
    {
        const float RowHeight = 68f;
        const float FirstRowY = 84f;
        const float FirstColX = 250f;
        const float ColWidth = 146f;
        const float RigHeight = 58f;

        window.Device.SetClearColor(new Vector4(0.07f, 0.09f, 0.12f, 1.0f));
        window.Device.Clear(ClearBufferMask.ColorBufferBit);

        geom.Begin(ortho);
        for (int row = 0; row < lineup.Length; row++)
        {
            for (int col = 0; col < s_phaseSamples.Length; col++)
            {
                float bx = FirstColX + (col * ColWidth);
                float by = FirstRowY + (row * RowHeight) + 48f;
                geom.FillRectangle(new RectangleF(bx - 70f, by - 56f, 140f, 64f), new ColorRgba(20, 26, 38, 255));
            }
        }

        for (int row = 0; row < lineup.Length; row++)
        {
            (ushort archId, byte kind, byte shape, float worldRadius, float halfDeg, _) = lineup[row];
            if (!art.TryGetMonster(archId, out CompiledVectorRig? rig))
            {
                continue;
            }

            string clip = MonsterAttackAnimator.ResolveClip(rig, archId, kind);
            AttackPhaseMarks marks = rig.TryGetAnimation(clip, out CompiledVectorAnimation? anim) ? anim.PhaseMarks : AttackPhaseMarks.Default;
            float footY = FirstRowY + (row * RowHeight) + 48f;
            float worldToSheet = RigHeight / MathF.Max(1f, rig.WorldHeight);
            float radius = MathF.Min(40f, worldRadius * worldToSheet); // clamped so neighbouring cells stay readable
            for (int col = 0; col < s_phaseSamples.Length; col++)
            {
                (byte phase, float progress, _) = s_phaseSamples[col];
                Vector2 foot = new(FirstColX + (col * ColWidth), footY);
                Vector2 target = kind == EntityAttackPhase.KindProjectile ? foot + new Vector2(62f, -4f) : foot + new Vector2(MathF.Min(46f, radius + 10f), 0f);
                AttackPhaseView view = new(kind, phase, progress, target, Vector2.UnitX, radius, halfDeg * (MathF.PI / 180f), shape);
                AttackTelegraphRenderer.Draw(geom, foot, view, 0.2f + (col * 0.37f));
                geom.FillCircle(foot, 9f, new ColorRgba(0, 0, 0, 90), 14);
                VectorRigRenderer.DrawEntityRig(
                    geom,
                    rig,
                    foot,
                    Direction8.East,
                    clip,
                    MonsterAttackAnimator.ToClipTime(phase, progress, in marks),
                    isNormalized: true,
                    targetHeightOverride: RigHeight);
            }
        }

        geom.End();

        sprites.Begin(DepthSortMode.Immediate, transformMatrix: ortho);
        ColorRgba black = new(0, 0, 0, 255);
        ColorRgba gold = new(250, 204, 21, 255);
        ColorRgba white = new(241, 245, 249, 255);
        ColorRgba cyan = new(125, 211, 252, 255);
        sprites.DrawOutlinedString(font, title, new Vector2(24f, 18f), gold, black, 1, 1.2f);
        for (int col = 0; col < s_phaseSamples.Length; col++)
        {
            sprites.DrawOutlinedString(font, s_phaseSamples[col].Label, new Vector2(FirstColX - 44f + (col * ColWidth), 62f), cyan, black, 1, 0.85f);
        }

        for (int row = 0; row < lineup.Length; row++)
        {
            (ushort archId, byte kind, _, _, _, string label) = lineup[row];
            string clip = art.TryGetMonster(archId, out CompiledVectorRig? rig) ? MonsterAttackAnimator.ResolveClip(rig, archId, kind) : "-";
            float y = FirstRowY + (row * RowHeight) + 10f;
            sprites.DrawOutlinedString(font, label, new Vector2(16f, y), white, black, 1, 0.9f);
            sprites.DrawOutlinedString(font, $"clip: {clip}", new Vector2(16f, y + 20f), gold, black, 1, 0.8f);
        }

        sprites.End();
        ScreenshotHelper.Capture(window.Device.Gl, window.Width, window.Height, path);
    }

    private static void CaptureWaterCreatures(
        IGameWindow window,
        CarbonVectorArtLibrary art,
        GeometryBatch geom,
        SpriteBatch sprites,
        BitmapFont font,
        Matrix4x4 ortho,
        string outputDir)
    {
        int w = window.Width;
        int h = window.Height;
        window.Device.SetClearColor(new Vector4(0.03f, 0.12f, 0.18f, 1.0f));
        window.Device.Clear(ClearBufferMask.ColorBufferBit);

        geom.Begin(ortho);
        float step = w / (float)(s_waterLineup.Length + 1);
        for (int i = 0; i < s_waterLineup.Length; i++)
        {
            (ushort archId, string atkClip, _, _) = s_waterLineup[i];
            if (!art.TryGetMonster(archId, out CompiledVectorRig? rig))
            {
                continue;
            }

            float cx = step * (i + 1);
            geom.FillCircle(new Vector2(cx, 258f), 40f, new ColorRgba(14, 64, 84, 230), 20);
            VectorRigRenderer.DrawEntityRig(geom, rig, new Vector2(cx, 258f), Direction8.South, "idle", 0.25f, isNormalized: false, targetHeightOverride: 120f);
            geom.FillCircle(new Vector2(cx, 540f), 40f, new ColorRgba(10, 40, 66, 230), 20);
            VectorRigRenderer.DrawEntityRig(geom, rig, new Vector2(cx, 540f), Direction8.East, atkClip, 0.5f, isNormalized: true, targetHeightOverride: 120f);
        }

        geom.End();

        sprites.Begin(DepthSortMode.Immediate, transformMatrix: ortho);
        ColorRgba black = new(0, 0, 0, 255);
        ColorRgba white = new(241, 245, 249, 255);
        ColorRgba cyan = new(103, 232, 249, 255);
        sprites.DrawOutlinedString(font, "CARBON WATER CREATURES (ADR-032 D14)", new Vector2(28f, 24f), cyan, black, 1, 1.4f);
        sprites.DrawOutlinedString(font, "TOP ROW: IDLE   |   BOTTOM ROW: ATTACK", new Vector2(28f, 52f), white, black, 1, 1.0f);
        for (int i = 0; i < s_waterLineup.Length; i++)
        {
            (_, string atkClip, string name, string habitat) = s_waterLineup[i];
            float cx = step * (i + 1);
            sprites.DrawOutlinedString(font, name, new Vector2(cx - 52f, 306f), white, black, 1, 1.0f);
            sprites.DrawOutlinedString(font, habitat, new Vector2(cx - 52f, 328f), cyan, black, 1, 0.85f);
            sprites.DrawOutlinedString(font, $"Clip: {atkClip}", new Vector2(cx - 48f, 592f), new ColorRgba(125, 211, 252, 255), black, 1, 0.9f);
        }

        sprites.End();
        ScreenshotHelper.Capture(window.Device.Gl, w, h, Path.Combine(outputDir, "water_creatures.bmp"));
    }
}

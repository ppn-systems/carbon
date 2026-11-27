// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Capture;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Desktop.Rendering.World;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Items.Races;
using Silk.NET.OpenGL;

namespace Carbon.Desktop.Capture;

/// <summary>
/// Body type sheet (<c>hero_bodies.bmp</c> from <c>--showcase</c>, ADR-042 D11): every body of every race in the shipped
/// <c>races.json</c>, through the same clips and equipment, so a reviewer sees that each body plays every shared clip and
/// wears the same gear. Rows are bodies (bare, then geared); columns are poses.
/// </summary>
internal static class HeroBodySheet
{
    private const float Zoom = 2f;
    private const float LabelWidth = 170f;
    private const float ColWidth = 110f;
    private const float FirstRowY = 64f;
    private const float RowHeight = 80f;
    private const float FootRadius = 7f;

    // Item ids of the shipped catalogue (items.json), as in HeroAttackFilmstrip: an offline capture has no config sync.
    private const uint WoodAxe = 1U;
    private const uint IronSword = 3U;
    private const ushort LeatherCap = 13;
    private const ushort LeatherBoots = 16;
    private const ushort IronChestplate = 18;

    // (label, clip or null for an attack, attack kind, held item, direction, normalized progress or loop time)
    private static readonly (string Label, string? Clip, ActionAnimationKind Kind, uint ItemId, Direction8 Dir, float T)[] s_columns =
    [
        ("idle S", "idle", ActionAnimationKind.None, 0U, Direction8.South, 0.2f),
        ("walk S", "walk", ActionAnimationKind.None, 0U, Direction8.South, 0.12f),
        ("walk E", "walk", ActionAnimationKind.None, 0U, Direction8.East, 0.30f),
        ("north", "idle", ActionAnimationKind.None, 0U, Direction8.North, 0.2f),
        ("sword", null, ActionAnimationKind.SwordSlash, IronSword, Direction8.East, 0.45f),
        ("axe", null, ActionAnimationKind.AxeChop, WoodAxe, Direction8.East, 0.5f),
        ("swim", "swim", ActionAnimationKind.None, 0U, Direction8.East, 0.25f),
        ("dive", "dive", ActionAnimationKind.None, 0U, Direction8.East, 0.25f),
        ("downed", "downed", ActionAnimationKind.None, 0U, Direction8.South, 1f),
    ];

    /// <summary>Renders the sheet and captures it to <paramref name="path"/>.</summary>
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
        // Offline capture tool: no server, so the build-time races.json (the server sends the same document).
        RaceCatalog races = RaceCatalog.LoadShippedDefaults();
        List<(string Label, CharacterLook Look, bool Geared)> rows = [];
        foreach (RaceDefinition race in races.Races)
        {
            foreach (BodyTypeDefinition body in race.Bodies)
            {
                byte[] options = new byte[race.Options.Count];
                for (int i = 0; i < options.Length; i++)
                {
                    options[i] = (byte)(((body.Id * 2) + i) % race.Options[i].Choices.Count);
                }

                CharacterLook look = new(race.Id, body.Id, options);
                rows.Add(($"{race.Code}/{body.Code}", look, false));
                rows.Add(($"{race.Code}/{body.Code} + gear", look, true));
            }
        }

        window.Device.SetClearColor(new Vector4(0.07f, 0.09f, 0.12f, 1.0f));
        window.Device.Clear(ClearBufferMask.ColorBufferBit);
        VisualEquipmentLoadout gear = new(
            LeatherCap,
            IronChestplate,
            0,
            LeatherBoots,
            0,
            0);

        geom.Begin(Matrix4x4.CreateScale(Zoom, Zoom, 1f) * ortho);
        for (int row = 0; row < rows.Count; row++)
        {
            (_, CharacterLook look, bool geared) = rows[row];
            if (!CharacterLookResolver.TryResolve(art, races, look, out CompiledVectorRig? rig, out CharacterPalette palette) || rig is null)
            {
                continue;
            }

            for (int col = 0; col < s_columns.Length; col++)
            {
                (_, string? clip, ActionAnimationKind kind, uint itemId, Direction8 dir, float t) = s_columns[col];
                float cellX = LabelWidth + (col * ColWidth);
                float cellY = FirstRowY + (row * RowHeight);
                geom.FillRectangle(new RectangleF((cellX + 3f) / Zoom, (cellY + 3f) / Zoom, (ColWidth - 6f) / Zoom, (RowHeight - 6f) / Zoom), new ColorRgba(20, 26, 38, 255));
                Vector2 foot = new((cellX + (ColWidth * 0.45f)) / Zoom, (cellY + (RowHeight * 0.82f)) / Zoom);
                geom.FillCircle(new Vector2(foot.X + 1.5f, foot.Y + 1.5f), FootRadius * 1.18f, new ColorRgba(0, 0, 0, 88), 12);

                CompiledItemVectorArt? item = null;
                if (itemId != 0U)
                {
                    _ = art.TryGetItem(itemId, out item);
                }

                ActionState action = kind == ActionAnimationKind.None ? default : new(kind, 1f, 1f - t, Vector2.UnitX, itemId, 0);
                string shown = clip ?? WorldSceneRenderer.ResolveCharacterClip(action, 0f, moving: false);
                bool normalized = clip is null or "downed";
                VectorRigRenderer.DrawEntityRig(
                    geom,
                    rig,
                    foot,
                    dir,
                    shown,
                    normalized ? (clip is null ? action.Progress : t) : t,
                    isNormalized: normalized,
                    equippedItem: item,
                    action: action,
                    palette: palette,
                    equipment: geared ? gear with { WeaponId = (ushort)itemId } : default,
                    art: art);
            }
        }

        geom.End();

        sprites.Begin(DepthSortMode.Immediate, transformMatrix: ortho);
        ColorRgba black = new(0, 0, 0, 255);
        ColorRgba gold = new(250, 204, 21, 255);
        ColorRgba white = new(241, 245, 249, 255);
        ColorRgba cyan = new(125, 211, 252, 255);
        sprites.DrawOutlinedString(font, "BODY TYPES: SHARED SKELETON, CLIPS AND GEAR (ADR-042, WORLD SCALE x2)", new Vector2(16f, 14f), gold, black, 1, 1.2f);
        for (int col = 0; col < s_columns.Length; col++)
        {
            sprites.DrawOutlinedString(font, s_columns[col].Label, new Vector2(LabelWidth + (col * ColWidth) + 24f, 44f), cyan, black, 1, 0.85f);
        }

        for (int row = 0; row < rows.Count; row++)
        {
            sprites.DrawOutlinedString(font, rows[row].Label, new Vector2(12f, FirstRowY + (row * RowHeight) + 30f), white, black, 1, 0.85f);
        }

        sprites.End();
        ScreenshotHelper.Capture(window.Device.Gl, window.Width, window.Height, path);
    }
}

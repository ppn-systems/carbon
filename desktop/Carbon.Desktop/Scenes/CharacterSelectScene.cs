// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Capture;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Textures;
using Carbon.Graphics.Scenes.Base;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Input;
using Carbon.Items.Catalog;
using Carbon.Items.Races;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Carbon.Desktop.Rendering.Fonts;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// Two-mode Hero Roster &amp; Character Creator scene framed with standardized 9-slice <see cref="CarbonUiSkin"/>,
/// featuring 4-direction live hero turntable, a classless fresh-start note (ADR-030),
/// and appearance trait customization.
/// </summary>
public sealed class CharacterSelectScene : GameScene
{
    private static readonly Direction8[] s_turnDirs = [Direction8.South, Direction8.West, Direction8.North, Direction8.East];
    private static readonly uint[] s_classWeapons = [StarterGameCatalog.IronSword, StarterGameCatalog.HuntingBow, StarterGameCatalog.ArcaneStaff];
    private static readonly string[] s_freshStartLines =
    [
        "character_creator.fresh_start.line1",
        "character_creator.fresh_start.line2",
        "character_creator.fresh_start.line3",
        "character_creator.fresh_start.line4",
        "character_creator.fresh_start.line5",
        "character_creator.fresh_start.line6",
        "character_creator.fresh_start.line7",
    ];

    private readonly MenuFlow _flow;
    private readonly IWorldSession _session;
    private readonly CharacterRoster _roster = new();
    private readonly HeroAppearance _appearance = new();
    private readonly CharacterRosterRequests _requests;

    private readonly MenuTextField _createNameField;
    private readonly MenuButton _enterWorldButton = new(GameText.Get("character.enter_world"), MenuButtonStyle.SecondaryEmerald);
    private readonly MenuButton _openCreatorButton = new(GameText.Get("character.new_hero"), MenuButtonStyle.PrimaryGold);
    private readonly MenuButton _backToRosterButton = new(GameText.Get("character.back"), MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _createButton = new(GameText.Get("character.create"), MenuButtonStyle.PrimaryGold);
    private readonly MenuButton _turnLeftBtn = new(GameText.Get("character.turn_left"), MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _turnRightBtn = new(GameText.Get("character.turn_right"), MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _randomizeBtn = new(GameText.Get("character.randomize"), MenuButtonStyle.NeutralSlate);
    private readonly MenuButton[] _optionPrevButtons = CreateArrowButtons("<");
    private readonly MenuButton[] _optionNextButtons = CreateArrowButtons(">");
    private readonly MenuButton _logoutButton = new(GameText.Get("character.logout"), MenuButtonStyle.DangerRed);
    private readonly RectangleF[] _slotBounds = new RectangleF[CharacterRoster.MaxHeroes];
    private RectangleF _freshStartBounds;

    private GeometryBatch? _geometryBatch;
    private BitmapFont? _font;
    private Texture2D? _whitePixel;
    private CarbonUiSkin? _uiSkin;
    private IVectorArtRepository? _vectorArt;
    private IInputManager? _boundInput;

    private int _turnIndex;
    private bool _isCreatorMode;

    private float _elapsed;
    private float _backspaceHoldTime;
    private float _backspaceRepeatTime;
    private readonly MenuSceneCapture _capture;
    private readonly CachedText _headerText = new();
    private readonly CachedText _netText = new();
    private readonly CachedText[] _optionRowText = [new(), new(), new(), new()];
    private readonly CachedText _dossierText = new();
    private readonly CachedText _positionText = new();
    private readonly CachedText[] _rosterRowText = [new(), new(), new(), new()];

    /// <summary>
    /// Initializes a new instance of the <see cref="CharacterSelectScene"/> class.
    /// </summary>
    internal CharacterSelectScene(MenuFlow flow)
        : base("CharacterSelect")
    {
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _session = flow.Session;
        _requests = new CharacterRosterRequests(_session, flow.SignOutAsync, _roster);
        DesktopOptions options = flow.Options;
        _capture = new MenuSceneCapture(options, "characters");

        _createNameField = new MenuTextField(GameText.Get("character.field.name"), GameText.Get("character.hint.name"), options.CharacterName, isPassword: false, maxLength: 20)
        {
            CharacterFilter = static ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == ' ',
        };
    }

    /// <summary>Gets the status line under the panels (behaviour tests).</summary>
    internal string StatusMessage => _requests.StatusMessage;

    /// <summary>Gets whether the status line is an error (behaviour tests).</summary>
    internal bool StatusIsError => _requests.StatusIsError;

    /// <summary>Gets whether the creator panel is shown instead of the roster (behaviour tests).</summary>
    internal bool IsCreatorMode => _isCreatorMode;

    /// <summary>Gets the heroes last loaded from the server (behaviour tests).</summary>
    internal IReadOnlyList<CharacterRosterEntry> Characters => _roster.Heroes;

    /// <summary>Gets the selected roster row (behaviour tests).</summary>
    internal int SelectedIndex => _roster.SelectedIndex;

    /// <summary>Gets the look the creator would send (behaviour tests).</summary>
    internal CharacterLook Look => _appearance.Look;

    /// <summary>Gets how many option rows the creator panel has room for.</summary>
    internal static int VisibleOptionRows => 4;

    /// <summary>Gets how many option rows the selected race fills (at most <see cref="VisibleOptionRows"/>).</summary>
    private int ShownOptionRows => Math.Min(_appearance.OptionCount, VisibleOptionRows);

    /// <summary>Gets the preview turntable direction index (behaviour tests).</summary>
    internal int TurnIndex => _turnIndex;

    /// <inheritdoc/>
    public override void Load(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Load(context);

        _geometryBatch = new GeometryBatch(context.Device);
        (_font, _whitePixel) = GameFontLoader.Load(context.Device);
        _uiSkin = CarbonUiSkin.Load(context.Device);
        _vectorArt = CarbonVectorArtLibrary.LoadOrDefault();
    }

    /// <inheritdoc/>
    public override void Enter(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Enter(context);

        this.AttachInput(context.Input);
        _requests.LoadRoster();
    }

    /// <inheritdoc/>
    public override void Leave(SceneContext context)
    {
        this.DetachInput();
        base.Leave(context);
    }

    /// <inheritdoc/>
    public override void Update(SceneContext context, double dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Update(context, dt);

        _elapsed += (float)dt;
        _appearance.Bind(_session.Races.Current);
        this.PollRequests(context);

        if (_requests.IsSigningOut || _requests.IsEnteringWorld)
        {
            return;
        }

        int width = context.WindowWidth;
        int height = context.WindowHeight;
        this.LayoutControls(width, height);

        KeyboardSnapshot kb = context.Input.Keyboard;
        MouseSnapshot mouse = context.Input.Mouse;
        bool busy = _requests.IsLoadingRoster || _requests.IsCreating;

        if (kb.IsKeyPressed(Key.Escape))
        {
            if (_createNameField.IsFocused)
            {
                _createNameField.IsFocused = false;
            }
            else if (_isCreatorMode && _roster.Count > 0)
            {
                _isCreatorMode = false;
            }
            else
            {
                this.BeginLogout();
                return;
            }
        }

        if (!_isCreatorMode && _roster.Count > 0)
        {
            if (kb.IsKeyPressed(Key.Up))
            {
                _roster.SelectPrevious();
            }
            else if (kb.IsKeyPressed(Key.Down))
            {
                _roster.SelectNext();
            }
        }

        this.HandleBackspaceRepeat(kb, (float)dt);

        bool leftDown = mouse.IsButtonDown(MouseButton.Left);
        bool leftPressed = mouse.IsButtonPressed(MouseButton.Left);
        Vector2 mousePos = mouse.Position;

        _logoutButton.IsEnabled = !busy;
        _turnLeftBtn.IsEnabled = !busy;
        _turnRightBtn.IsEnabled = !busy;

        if (_logoutButton.Update(mousePos, leftDown, leftPressed))
        {
            this.BeginLogout();
            return;
        }

        if (_turnLeftBtn.Update(mousePos, leftDown, leftPressed))
        {
            _turnIndex = (_turnIndex + s_turnDirs.Length - 1) % s_turnDirs.Length;
        }

        if (_turnRightBtn.Update(mousePos, leftDown, leftPressed))
        {
            _turnIndex = (_turnIndex + 1) % s_turnDirs.Length;
        }

        if (_isCreatorMode)
        {
            this.UpdateCreatorMode(kb, mousePos, leftDown, leftPressed, (float)dt, busy);
        }
        else
        {
            this.UpdateRosterMode(kb, mousePos, leftDown, leftPressed, busy);
        }
    }

    /// <inheritdoc/>
    public override void Render(SceneContext context, SpriteBatch spriteBatch, double dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(spriteBatch);

        if (_geometryBatch is null || _font is null || _uiSkin is null)
        {
            return;
        }

        int width = context.WindowWidth;
        int height = context.WindowHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        context.Device.SetClearColor(new Vector4(0.04f, 0.06f, 0.11f, 1f));
        context.Device.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        this.LayoutControls(width, height);
        Matrix4x4 proj = Matrix4x4.CreateOrthographicOffCenter(0f, width, height, 0f, -1f, 1f);

        const float totalW = 920f;
        const float totalH = 520f;
        float ox = MathF.Floor((width - totalW) * 0.5f);
        float oy = MathF.Floor((height - totalH) * 0.5f) + 10f;

        RectangleF mainCard = new(ox, oy, totalW, totalH);
        RectangleF stageCard = new(ox + 308f, oy + 68f, 268f, 384f);
        RectangleF rightCard = new(ox + 592f, oy + 68f, 306f, 384f);
        RectangleF statusPill = new(ox + 22f, oy + totalH - 46f, totalW - 44f, 30f);

        // Pass 1: Twilight horizon backdrop
        _geometryBatch.Begin(proj);
        MenuBackdropRenderer.Draw(_geometryBatch, width, height, _elapsed);
        _geometryBatch.End();

        // Pass 2: Standardized 9-Slice Windows, Cards, Hero Slots & Buttons
        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: proj);
        _uiSkin.DrawWindow(spriteBatch, in mainCard, CarbonUiPalette.BorderOuter, bodyAlpha: 242);
        _uiSkin.DrawCard(spriteBatch, in stageCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 234);
        _uiSkin.DrawCard(spriteBatch, in rightCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 234);

        if (_isCreatorMode)
        {
            _uiSkin.DrawPanel(spriteBatch, UiPanelStyle.DoubleCard, in _freshStartBounds, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);

            for (int row = 0; row < this.ShownOptionRows; row++)
            {
                _optionPrevButtons[row].DrawSkin(spriteBatch, _uiSkin);
                _optionNextButtons[row].DrawSkin(spriteBatch, _uiSkin);
            }

            _randomizeBtn.DrawSkin(spriteBatch, _uiSkin);
            _createNameField.DrawSkin(spriteBatch, _uiSkin);
            _createButton.DrawSkin(spriteBatch, _uiSkin);
            if (_roster.Count > 0)
            {
                _backToRosterButton.DrawSkin(spriteBatch, _uiSkin);
            }
        }
        else
        {
            for (int i = 0; i < CharacterRoster.MaxHeroes; i++)
            {
                bool occupied = i < _roster.Count;
                bool sel = occupied && i == _roster.SelectedIndex;
                ColorRgba border = sel ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle;
                ColorRgba body = sel ? CarbonUiPalette.SlotSelectedFill : new ColorRgba(CarbonUiPalette.SlotFill.R, CarbonUiPalette.SlotFill.G, CarbonUiPalette.SlotFill.B, occupied ? (byte)234 : (byte)175);
                _uiSkin.DrawPanel(spriteBatch, UiPanelStyle.DoubleCard, in _slotBounds[i], body, border);
                if (occupied)
                {
                    RectangleF pBox = new(_slotBounds[i].X + 10f, _slotBounds[i].Y + 12f, 52f, 52f);
                    _uiSkin.DrawSlot(spriteBatch, in pBox, CarbonUiPalette.SlotFill, sel ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderOuter);
                }
            }

            if (_roster.HasFreeSlot)
            {
                _openCreatorButton.DrawSkin(spriteBatch, _uiSkin);
            }

            _enterWorldButton.DrawSkin(spriteBatch, _uiSkin);
        }

        _turnLeftBtn.DrawSkin(spriteBatch, _uiSkin);
        _turnRightBtn.DrawSkin(spriteBatch, _uiSkin);
        _logoutButton.DrawSkin(spriteBatch, _uiSkin);
        _uiSkin.DrawInset(spriteBatch, in statusPill, CarbonUiPalette.SlotFill, _requests.StatusIsError ? CarbonUiPalette.DangerBorder : CarbonUiPalette.BorderSubtle);
        spriteBatch.End();

        // Pass 3: Live Vector Hero Pedestal, Portraits & Cursor
        _geometryBatch.Begin(proj);
        Direction8 previewDir = s_turnDirs[_turnIndex];
        float stageCx = stageCard.X + (stageCard.Width * 0.5f);
        float stageCy = stageCard.Y + 240f;
        _geometryBatch.FillCircle(new Vector2(stageCx, stageCy), 44f, new ColorRgba(6, 10, 18, 145), 22);
        _geometryBatch.DrawCircle(new Vector2(stageCx, stageCy), 45f, CarbonUiPalette.BorderOuter, 22, 1.8f);

        CharacterLook previewLook = _isCreatorMode || !_roster.HasSelection
            ? _appearance.Look
            : _roster.Heroes[_roster.SelectedIndex].Look;
        RaceCatalog? races = _session.Races.Current;
        if (_vectorArt is not null &&
            CharacterLookResolver.TryResolve(_vectorArt, races, previewLook, out CompiledVectorRig? stageRig, out CharacterPalette stagePalette) &&
            stageRig is not null)
        {
            CompiledItemVectorArt? weaponArt = null;
            VisualEquipmentLoadout previewLoadout = default;
            // A new hero arrives with nothing (ADR-030); a hero created with a class shows that class's starter kit.
            HeroArchetypeClass previewClass = !_isCreatorMode && _roster.HasSelection
                ? _roster.Heroes[_roster.SelectedIndex].Class
                : HeroArchetypeClass.None;
            if ((int)previewClass < s_classWeapons.Length)
            {
                uint starterWeaponId = s_classWeapons[(int)previewClass];
                _ = _vectorArt.TryGetItem(starterWeaponId, out weaponArt);
                previewLoadout = new VisualEquipmentLoadout(
                    HeadId: (ushort)StarterGameCatalog.LeatherCap,
                    ChestId: (ushort)StarterGameCatalog.LeatherTunic,
                    LegsId: 0,
                    FeetId: (ushort)StarterGameCatalog.LeatherBoots,
                    WeaponId: (ushort)starterWeaponId,
                    WeaponUpgradeLevel: 0);
            }

            VectorRigRenderer.DrawEntityRig(
                _geometryBatch,
                stageRig,
                new Vector2(stageCx, stageCy + 10f),
                previewDir,
                "idle",
                _elapsed,
                isNormalized: false,
                targetHeightOverride: 96f,
                hitFlash: 0f,
                equippedItem: weaponArt,
                action: default,
                swingTimer: 0f,
                palette: stagePalette,
                equipment: previewLoadout,
                art: _vectorArt);
        }

        if (_isCreatorMode)
        {
            _createNameField.DrawCursorGeometry(_geometryBatch, _font);
        }
        else if (_vectorArt is not null)
        {
            for (int i = 0; i < _roster.Count; i++)
            {
                if (CharacterLookResolver.TryResolve(_vectorArt, races, _roster.Heroes[i].Look, out CompiledVectorRig? cardRig, out CharacterPalette cardPalette) &&
                    cardRig is not null)
                {
                    RectangleF b = _slotBounds[i];
                    VectorRigRenderer.DrawPortraitRig(
                        _geometryBatch,
                        cardRig,
                        new Vector2(b.X + 36f, b.Y + 38f),
                        boxSize: 42f,
                        Direction8.South,
                        "idle",
                        _elapsed + (i * 0.25f),
                        palette: cardPalette,
                        art: _vectorArt);
                }
            }
        }

        _geometryBatch.End();

        // Pass 4: Text & Typography
        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: proj);
        ColorRgba black = CarbonUiPalette.TextOutline;

        string header = _isCreatorMode
            ? GameText.Get("character.header.creator")
            : _headerText.Format("character.header.roster", new TextArg("count", _roster.Count), new TextArg("max", CharacterRoster.MaxHeroes));
        spriteBatch.DrawOutlinedString(_font, header, new Vector2(ox + 22f, oy + 18f), CarbonUiPalette.TextWarm, black, 2, 1.5f);
        spriteBatch.DrawOutlinedString(_font, _netText.Format("character.connection", new TextArg("status", _session.ConnectionLabel)), new Vector2(ox + 686f, oy + 22f), CarbonUiPalette.TextMuted, black, 1, 1f);

        string stageTitle = GameText.Get("character.stage");
        TextMeasureResult stSize = _font.MeasureString(stageTitle, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(_font, stageTitle, new Vector2(MathF.Floor(stageCard.X + ((stageCard.Width - stSize.Width) * 0.5f)), stageCard.Y + 14f), CarbonUiPalette.TextPrimary, black, 1, 1f);

        if (_isCreatorMode)
        {
            spriteBatch.DrawOutlinedString(_font, GameText.Get("character_creator.fresh_start"), new Vector2(_freshStartBounds.X + 16f, _freshStartBounds.Y + 16f), CarbonUiPalette.TextWarm, black, 1, 1.25f);
            for (int line = 0; line < s_freshStartLines.Length; line++)
            {
                string text = CarbonUiSkin.TruncateToWidth(_font, GameText.Get(s_freshStartLines[line]), _freshStartBounds.Width - 32f, 1f);
                spriteBatch.DrawOutlinedString(_font, text, new Vector2(_freshStartBounds.X + 16f, _freshStartBounds.Y + 58f + (line * 30f)), CarbonUiPalette.TextMuted, black, 1, 1f);
            }

            spriteBatch.DrawOutlinedString(_font, GameText.Get("character.appearance"), new Vector2(rightCard.X + 16f, rightCard.Y + 14f), CarbonUiPalette.TextWarm, black, 1, 1f);
            RaceDefinition? race = _appearance.Race;
            for (int row = 0; row < this.ShownOptionRows; row++)
            {
                AppearanceOptionDefinition option = race!.Options[row];
                string label = _optionRowText[row].Format(
                    "appearance.row",
                    new TextArg("label", GameText.Get(option.NameKey)),
                    new TextArg("value", GameText.Get(option.Choices[_appearance.GetChoice(row)].NameKey)));
                spriteBatch.DrawOutlinedString(_font, label, new Vector2(rightCard.X + 52f, rightCard.Y + 49f + (row * 34f)), CarbonUiPalette.TextPrimary, black, 1, 1f);
                _optionPrevButtons[row].DrawText(spriteBatch, _font);
                _optionNextButtons[row].DrawText(spriteBatch, _font);
            }

            _randomizeBtn.DrawText(spriteBatch, _font);
            _createNameField.DrawText(spriteBatch, _font);
            _createButton.DrawText(spriteBatch, _font);
            if (_roster.Count > 0)
            {
                _backToRosterButton.DrawText(spriteBatch, _font);
            }
        }
        else
        {
            for (int i = 0; i < CharacterRoster.MaxHeroes; i++)
            {
                RectangleF b = _slotBounds[i];
                if (i < _roster.Count)
                {
                    CharacterRosterEntry c = _roster.Heroes[i];
                    ColorRgba nc = i == _roster.SelectedIndex ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextPrimary;
                    string clsLabel = GameText.ClassName(c.Class);
                    spriteBatch.DrawOutlinedString(_font, CarbonUiSkin.TruncateToWidth(_font, c.Name, b.Width - 82f, 1.25f), new Vector2(b.X + 70f, b.Y + 14f), nc, black, 1, 1.25f);
                    spriteBatch.DrawOutlinedString(_font, _rosterRowText[i].Format("character.roster_row", new TextArg("class", clsLabel), new TextArg("level", c.Level)), new Vector2(b.X + 70f, b.Y + 42f), CarbonUiPalette.TextMuted, black, 1, 1f);
                }
                else
                {
                    spriteBatch.DrawOutlinedString(_font, GameText.Get("character.empty_slot"), new Vector2(b.X + 26f, b.Y + 30f), CarbonUiPalette.TextMuted, black, 1, 1f);
                }
            }

            spriteBatch.DrawOutlinedString(_font, GameText.Get("character.dossier"), new Vector2(rightCard.X + 16f, rightCard.Y + 14f), CarbonUiPalette.TextWarm, black, 1, 1f);
            if (_roster.HasSelection)
            {
                CharacterRosterEntry sel = _roster.Heroes[_roster.SelectedIndex];
                string selCls = GameText.ClassName(sel.Class);
                spriteBatch.DrawOutlinedString(_font, CarbonUiSkin.TruncateToWidth(_font, sel.Name.ToUpperInvariant(), rightCard.Width - 32f, 1.5f), new Vector2(rightCard.X + 16f, rightCard.Y + 44f), CarbonUiPalette.TextPrimary, black, 1, 1.5f);
                spriteBatch.DrawOutlinedString(_font, _dossierText.Format("character.dossier_line", new TextArg("class", selCls), new TextArg("level", sel.Level), new TextArg("tier", sel.ZoneTier)), new Vector2(rightCard.X + 16f, rightCard.Y + 78f), CarbonUiPalette.TextWarm, black, 1, 1f);
                spriteBatch.DrawOutlinedString(_font, _positionText.Format("character.position", new TextArg("x", (int)sel.PositionX), new TextArg("y", (int)sel.PositionY)), new Vector2(rightCard.X + 16f, rightCard.Y + 102f), CarbonUiPalette.TextMuted, black, 1, 1f);
            }

            if (_roster.HasFreeSlot)
            {
                _openCreatorButton.DrawText(spriteBatch, _font);
            }

            _enterWorldButton.DrawText(spriteBatch, _font);
        }

        _turnLeftBtn.DrawText(spriteBatch, _font);
        _turnRightBtn.DrawText(spriteBatch, _font);
        _logoutButton.DrawText(spriteBatch, _font);

        ColorRgba statusColor = _requests.StatusIsError
            ? new ColorRgba(235, 130, 130, 255)
            : CarbonUiPalette.TextPrimary;
        string status = CarbonUiSkin.TruncateToWidth(_font, _requests.StatusMessage, statusPill.Width - 20f, 1f);
        spriteBatch.DrawOutlinedString(_font, status, new Vector2(statusPill.X + 10f, statusPill.Y + 7f), statusColor, black, 1, 1f);
        spriteBatch.End();

        base.Render(context, spriteBatch, dt);

        _capture.AfterRender(context, ready: !_requests.IsLoadingRoster && !_requests.IsCreating);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.DetachInput();
            _vectorArt = null;
            _uiSkin?.Dispose();
            _uiSkin = null;
            _geometryBatch?.Dispose();
            _geometryBatch = null;
            _font?.Dispose();
            _font = null;
            _whitePixel?.Dispose();
            _whitePixel = null;
        }

        base.Dispose(disposing);
    }

    private void UpdateRosterMode(KeyboardSnapshot kb, Vector2 mousePos, bool leftDown, bool leftPressed, bool busy)
    {
        _createNameField.IsFocused = false;
        if (leftPressed && !busy)
        {
            for (int i = 0; i < CharacterRoster.MaxHeroes; i++)
            {
                if (_slotBounds[i].Contains(mousePos) && !_roster.TrySelect(i) && _roster.HasFreeSlot)
                {
                    _isCreatorMode = true;
                    _createNameField.IsFocused = true;
                    return;
                }
            }
        }

        bool hasSelection = _roster.HasSelection;
        _enterWorldButton.IsEnabled = !busy && hasSelection;
        _openCreatorButton.IsEnabled = !busy && _roster.HasFreeSlot;

        if (_roster.HasFreeSlot && _openCreatorButton.Update(mousePos, leftDown, leftPressed))
        {
            _isCreatorMode = true;
            _createNameField.IsFocused = true;
            return;
        }

        bool enterKey = !busy && (kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter));
        if ((_enterWorldButton.Update(mousePos, leftDown, leftPressed) || enterKey) && hasSelection)
        {
            _requests.EnterWorld(_roster.Selected);
        }
    }

    private void UpdateCreatorMode(KeyboardSnapshot kb, Vector2 mousePos, bool leftDown, bool leftPressed, float dt, bool busy)
    {
        _createNameField.Update(dt, mousePos, leftPressed && !busy);
        for (int row = 0; row < VisibleOptionRows; row++)
        {
            _optionPrevButtons[row].IsEnabled = !busy;
            _optionNextButtons[row].IsEnabled = !busy;
        }

        _randomizeBtn.IsEnabled = !busy;
        _createButton.IsEnabled = !busy && _roster.HasFreeSlot;
        _backToRosterButton.IsEnabled = !busy && _roster.Count > 0;

        for (int row = 0; row < this.ShownOptionRows; row++)
        {
            if (_optionPrevButtons[row].Update(mousePos, leftDown, leftPressed))
            {
                _appearance.CycleOption(row, -1);
            }
            else if (_optionNextButtons[row].Update(mousePos, leftDown, leftPressed))
            {
                _appearance.CycleOption(row, 1);
            }
        }

        if (_randomizeBtn.Update(mousePos, leftDown, leftPressed))
        {
            _appearance.Randomize(Random.Shared);
        }

        if (_roster.Count > 0 && _backToRosterButton.Update(mousePos, leftDown, leftPressed))
        {
            _isCreatorMode = false;
            _createNameField.IsFocused = false;
            return;
        }

        bool enterKey = !busy && (kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter));
        if (_createButton.Update(mousePos, leftDown, leftPressed) || (enterKey && _createNameField.IsFocused && _roster.HasFreeSlot))
        {
            _requests.Create(_createNameField.Text, _appearance.Look);
        }
    }

    private void PollRequests(SceneContext context)
    {
        RosterPollResult result = _requests.Poll();
        if (result.Outcome == RosterRequestOutcome.SignedOut)
        {
            StartScene? next = null;
            try
            {
                next = new StartScene(_flow);
                context.SceneManager.ChangeScene(next, transitionDuration: 0.15f);
                next = null;
            }
            finally
            {
                next?.Dispose();
            }

            return;
        }

        if (result.Outcome == RosterRequestOutcome.EnteredWorld)
        {
            WorldGameplayScene? next = null;
            try
            {
                next = new WorldGameplayScene(_flow);
                context.SceneManager.ChangeScene(next, transitionDuration: 0.18f);
                next = null;
            }
            finally
            {
                next?.Dispose();
            }

            return;
        }

        if (result.HeroCreated)
        {
            _isCreatorMode = false;
            _createNameField.IsFocused = false;
        }

        if (result.RosterLoadedEmpty)
        {
            _isCreatorMode = true;
            _createNameField.IsFocused = true;
        }
    }

    private void LayoutControls(int width, int height)
    {
        const float totalW = 920f;
        const float totalH = 520f;
        float ox = MathF.Floor((width - totalW) * 0.5f);
        float oy = MathF.Floor((height - totalH) * 0.5f) + 10f;

        _logoutButton.Bounds = new RectangleF(ox + totalW - 122f, oy + 14f, 100f, 34f);
        _turnLeftBtn.Bounds = new RectangleF(ox + 324f, oy + 404f, 112f, 32f);
        _turnRightBtn.Bounds = new RectangleF(ox + 448f, oy + 404f, 112f, 32f);

        for (int i = 0; i < CharacterRoster.MaxHeroes; i++)
        {
            _slotBounds[i] = new RectangleF(ox + 22f, oy + 68f + (i * 84f), 270f, 76f);
        }

        _openCreatorButton.Bounds = new RectangleF(ox + 22f, oy + 408f, 270f, 44f);
        _enterWorldButton.Bounds = new RectangleF(ox + 608f, oy + 392f, 274f, 46f);

        _freshStartBounds = new RectangleF(ox + 22f, oy + 68f, 270f, 384f);

        float rx = ox + 592f;
        float ry = oy + 68f;
        for (int row = 0; row < VisibleOptionRows; row++)
        {
            _optionPrevButtons[row].Bounds = new RectangleF(rx + 16f, ry + 44f + (row * 34f), 28f, 26f);
            _optionNextButtons[row].Bounds = new RectangleF(rx + 262f, ry + 44f + (row * 34f), 28f, 26f);
        }

        _randomizeBtn.Bounds = new RectangleF(rx + 16f, ry + 184f, 274f, 32f);

        _createNameField.Bounds = new RectangleF(rx + 16f, ry + 250f, 274f, 36f);
        if (_roster.Count > 0)
        {
            _createButton.Bounds = new RectangleF(rx + 16f, ry + 302f, 182f, 44f);
            _backToRosterButton.Bounds = new RectangleF(rx + 206f, ry + 302f, 84f, 44f);
        }
        else
        {
            _createButton.Bounds = new RectangleF(rx + 16f, ry + 302f, 274f, 44f);
        }
    }

    private void BeginLogout() => _requests.SignOut();

    private void HandleBackspaceRepeat(in KeyboardSnapshot kb, float dt)
    {
        if (kb.IsKeyPressed(Key.Backspace))
        {
            _createNameField.Backspace();
            _backspaceHoldTime = 0f;
            _backspaceRepeatTime = 0f;
            return;
        }

        if (kb.IsKeyDown(Key.Backspace))
        {
            _backspaceHoldTime += dt;
            if (_backspaceHoldTime >= 0.38f)
            {
                _backspaceRepeatTime += dt;
                if (_backspaceRepeatTime >= 0.055f)
                {
                    _backspaceRepeatTime = 0f;
                    _createNameField.Backspace();
                }
            }
        }
        else
        {
            _backspaceHoldTime = 0f;
            _backspaceRepeatTime = 0f;
        }
    }

    private void OnCharacterReceived(char ch)
    {
        if (_isCreatorMode && !_requests.IsCreating && !_requests.IsEnteringWorld && !_requests.IsSigningOut)
        {
            _createNameField.AppendChar(ch);
        }
    }

    private void AttachInput(IInputManager input)
    {
        this.DetachInput();
        _boundInput = input;
        _boundInput.CharacterReceived += this.OnCharacterReceived;
    }

    private void DetachInput()
    {
        if (_boundInput is { } bound)
        {
            bound.CharacterReceived -= this.OnCharacterReceived;
            _boundInput = null;
        }
    }

    private static MenuButton[] CreateArrowButtons(string glyph)
    {
        MenuButton[] buttons = new MenuButton[VisibleOptionRows];
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i] = new MenuButton(glyph, MenuButtonStyle.NeutralSlate);
        }

        return buttons;
    }
}

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
using Carbon.Graphics.OpenGL.Abstractions;
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
    private readonly MenuPicker[] _rows = CreatePickers();
    private readonly MenuToggle _bodyToggle = new(string.Empty, string.Empty);
    private readonly MenuButton _logoutButton = new(GameText.Get("character.logout"), MenuButtonStyle.DangerRed);
    private readonly RectangleF[] _slotBounds = new RectangleF[CharacterRoster.MaxHeroes];
    private RectangleF _appearanceCard;
    private RectangleF _loreStrip;
    private int _pickerFocus = -1;
    private RectangleF _optionsBounds;

    private GeometryBatch? _geometryBatch;
    private ScaledFontCache? _fonts;
    private float? _uiScaleChoice;
    private int _canvasWidth;
    private int _canvasHeight;
    private CarbonUiSkin? _uiSkin;
    private IVectorArtRepository? _vectorArt;
    private IInputManager? _boundInput;

    private bool _enteringWorld;
    private int _optionScroll;
    private float _frameDt;
    private float _nameRejectedFor;
    private bool _createAttempted;
    private int _hoveredSlot = -1;
    private bool _isCreatorMode;

    private float _elapsed;
    private float _backspaceHoldTime;
    private float _backspaceRepeatTime;
    private readonly MenuSceneCapture _capture;
    private readonly CachedText _headerText = new();
    private readonly CachedText _netText = new();
    private readonly CachedText[] _optionRowText = [new(), new(), new(), new(), new(), new()];
    private readonly CachedText _dossierText = new();
    private readonly CachedText _positionText = new();
    private readonly CachedText[] _rosterRowText = [new(), new(), new(), new()];
    private readonly CachedText[] _traitIndexText = [new(), new(), new(), new(), new(), new()];
    private readonly HeroTurntable _turntable;

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
        _turntable = new HeroTurntable(options.MenuMotion);

        _createNameField = new MenuTextField(GameText.Get("character.field.name"), GameText.Get("character.hint.name"), options.CharacterName, isPassword: false, maxLength: AccountRules.MaxHeroName)
        {
            // The server takes 2-20 ASCII letters, digits or _ (no spaces, no accents): refuse the rest as it is typed.
            CharacterFilter = AccountRules.IsHeroNameChar,
            Icon = MenuFieldIcon.Hero,
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

    /// <summary>Gets how many selector rows the creator panel has room for: race, body type, then option rows.</summary>
    internal static int VisibleOptionRows => 6;

    /// <summary>Gets how many option rows fit under the race and body rows.</summary>
    internal static int VisibleAppearanceRows => VisibleOptionRows - FirstOptionRow;

    /// <summary>Gets the first option shown when the race has more options than fit (the mouse wheel scrolls them).</summary>
    internal int OptionScroll => _optionScroll;

    /// <summary>Gets how many selector rows are shown: race and body (once races.json has synced), then options.</summary>
    private int ShownOptionRows => _appearance.Race is null
        ? 0
        : FirstOptionRow + Math.Min(_appearance.OptionCount, VisibleAppearanceRows);

    private const int RaceRow = 0;
    private const int BodyRow = 1;
    private const int FirstOptionRow = 2;
    private const float SelectorRowStep = 36f;

    /// <summary>Gets the hero name field (behaviour tests read its text and feedback).</summary>
    internal MenuTextField HeroName => _createNameField;

    /// <summary>Gets the screen pixels per layout unit (docs/art/menu-ui.md rule 21).</summary>
    internal float UiScale { get; private set; } = 1f;

    /// <summary>Gets the main window rectangle from the last layout, in canvas units (tests check it fits).</summary>
    internal RectangleF PanelBounds { get; private set; }

    /// <summary>Gets the preview turntable direction index (behaviour tests).</summary>
    internal int TurnIndex => _turntable.Index;

    /// <inheritdoc/>
    public override void Load(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Load(context);

        _geometryBatch = new GeometryBatch(context.Device);
        _uiSkin = CarbonUiSkin.Load(context.Device);
        IGraphicsDevice device = context.Device;
        _fonts = new ScaledFontCache(scale => GameFontLoader.LoadFont(device, locale: null, scale));
        _uiScaleChoice = _flow.ReadUiScaleChoice();
        this.ApplyScale(context.WindowWidth, context.WindowHeight);
        _vectorArt = CarbonVectorArtLibrary.LoadOrDefault();
    }

    /// <inheritdoc/>
    public override void Enter(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Enter(context);

        this.AttachInput(context.Input);
        _flow.Backdrop.Resume();
        _requests.LoadRoster();
    }

    /// <inheritdoc/>
    public override void Leave(SceneContext context)
    {
        this.DetachInput();
        _flow.Backdrop.Pause();
        if (_enteringWorld)
        {
            // The menus' backdrop is not drawn in the world; free its meshes until the player returns.
            _flow.Backdrop.ReleaseGpu();
        }

        base.Leave(context);
    }

    /// <inheritdoc/>
    public override void Update(SceneContext context, double dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Update(context, dt);

        _elapsed += (float)dt;
        _frameDt = (float)dt;
        _nameRejectedFor = MathF.Max(0f, _nameRejectedFor - (float)dt);
        _turntable.Advance((float)dt);
        _flow.Backdrop.Advance((float)dt);
        bool firstCatalog = _appearance.Catalog is null;
        _appearance.Bind(_session.Races.Current);
        if (firstCatalog && _appearance.Catalog is not null && _flow.Options.CharacterBodyCode is { } bodyCode)
        {
            _ = _appearance.SelectBody(bodyCode); // CARBON_CHARACTER_BODY preselects a body (screenshot capture)
        }

        this.ScrollOptions(0);
        this.PollRequests(context);

        if (_requests.IsSigningOut || _requests.IsEnteringWorld)
        {
            return;
        }

        this.ApplyScale(context.WindowWidth, context.WindowHeight);
        this.LayoutControls(_canvasWidth, _canvasHeight);

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

        if (_isCreatorMode && _pickerFocus >= 0)
        {
            // A focused selector row takes Left/Right; Q/E still turn the hero.
            int step = kb.IsKeyPressed(Key.Left) ? -1 : kb.IsKeyPressed(Key.Right) ? 1 : 0;
            if (step != 0 && !busy && this.RowChoiceCount(_pickerFocus) > 1)
            {
                this.CycleRow(_pickerFocus, step);
            }

            if (kb.IsKeyPressed(Key.Q))
            {
                _turntable.Turn(-1);
            }
            else if (kb.IsKeyPressed(Key.E))
            {
                _turntable.Turn(1);
            }
        }
        else if (!_createNameField.IsFocused)
        {
            if (kb.IsKeyPressed(Key.Left) || kb.IsKeyPressed(Key.Q))
            {
                _turntable.Turn(-1);
            }
            else if (kb.IsKeyPressed(Key.Right) || kb.IsKeyPressed(Key.E))
            {
                _turntable.Turn(1);
            }
        }

        this.HandleBackspaceRepeat(kb, (float)dt);

        bool leftDown = mouse.IsButtonDown(MouseButton.Left);
        bool leftPressed = mouse.IsButtonPressed(MouseButton.Left);
        Vector2 mousePos = mouse.Position / this.UiScale; // window pixels to canvas units, where the hit rectangles live

        _logoutButton.IsEnabled = !busy;
        _turnLeftBtn.IsEnabled = !busy;
        _turnRightBtn.IsEnabled = !busy;

        if (_logoutButton.Update(mousePos, leftDown, leftPressed, _frameDt))
        {
            this.BeginLogout();
            return;
        }

        if (_turnLeftBtn.Update(mousePos, leftDown, leftPressed, _frameDt))
        {
            _turntable.Turn(-1);
        }

        if (_turnRightBtn.Update(mousePos, leftDown, leftPressed, _frameDt))
        {
            _turntable.Turn(1);
        }

        if (_isCreatorMode)
        {
            this.UpdateCreatorMode(kb, mousePos, mouse.ScrollDelta.Y, leftDown, leftPressed, (float)dt, busy);
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

        if (_geometryBatch is null || _fonts?.Font is null || _uiSkin is null)
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

        int windowWidth = width;
        int windowHeight = height;
        this.ApplyScale(windowWidth, windowHeight);
        BitmapFont font = _fonts.Font;
        width = _canvasWidth;
        height = _canvasHeight;
        this.LayoutControls(width, height);
        Matrix4x4 proj = Matrix4x4.CreateOrthographicOffCenter(0f, windowWidth / this.UiScale, windowHeight / this.UiScale, 0f, -1f, 1f);

        const float totalW = 920f;
        const float totalH = 580f;
        float ox = MathF.Floor((width - totalW) * 0.5f);
        float oy = MathF.Floor((height - totalH) * 0.5f) + 10f;

        RectangleF mainCard = new(ox, oy, totalW, totalH);
        RectangleF stageCard = new(ox + 308f, oy + 68f, 268f, 452f);
        RectangleF rightCard = new(ox + 592f, oy + 68f, 306f, 452f);
        RectangleF statusPill = new(ox + 22f, oy + totalH - 46f, totalW - 44f, 30f);

        // Pass 1: the menus' shared living backdrop (docs/art/menu-ui.md rule 15)
        _flow.Backdrop.Render(context.Device, _geometryBatch, windowWidth, windowHeight);

        // Pass 2: Standardized 9-Slice Windows, Cards, Hero Slots & Buttons
        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: proj);
        _uiSkin.DrawWindow(spriteBatch, in mainCard, CarbonUiPalette.BorderOuter, bodyAlpha: 242);
        _uiSkin.DrawCard(spriteBatch, in stageCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 234);
        _uiSkin.DrawCard(spriteBatch, in rightCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 234);

        if (_isCreatorMode)
        {
            _uiSkin.DrawCard(spriteBatch, in _appearanceCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 234);
            _uiSkin.DrawInset(spriteBatch, in _loreStrip, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);
            for (int row = 0; row < this.ShownOptionRows; row++)
            {
                if (row == BodyRow && this.BodyUsesToggle)
                {
                    _bodyToggle.DrawSkin(spriteBatch, _uiSkin);
                }
                else
                {
                    _rows[row].DrawSkin(spriteBatch, _uiSkin);
                }
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
                ColorRgba border = sel ? CarbonUiPalette.BorderActive : i == _hoveredSlot ? MenuPalette.HoverBorder : CarbonUiPalette.BorderSubtle;
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
        Direction8 previewDir = _turntable.Facing;
        float stageCx = stageCard.X + (stageCard.Width * 0.5f);
        float stageCy = stageCard.Y + 270f;
        _geometryBatch.FillCircle(new Vector2(stageCx, stageCy), 52f, new ColorRgba(212, 186, 130, 18), 26);
        _geometryBatch.FillCircle(new Vector2(stageCx, stageCy), 44f, new ColorRgba(6, 10, 18, 145), 22);
        _geometryBatch.DrawCircle(new Vector2(stageCx, stageCy), 45f, CarbonUiPalette.BorderOuter, 22, 1.8f);

        // Turntable facing: four dots under the pedestal, the current facing lit.
        for (int f = 0; f < HeroTurntable.FacingCount; f++)
        {
            Vector2 dot = new(stageCx - 21f + (f * 14f), stageCy + 68f);
            bool lit = f == _turntable.Index;
            _geometryBatch.FillCircle(dot, lit ? 3.5f : 2.5f, lit ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle, segments: 10);
        }

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
            _createNameField.DrawCursorGeometry(_geometryBatch, font);
            for (int row = 0; row < this.ShownOptionRows; row++)
            {
                if (row == BodyRow && this.BodyUsesToggle)
                {
                    _bodyToggle.DrawOverlay(_geometryBatch);
                }
                else
                {
                    _rows[row].DrawOverlay(_geometryBatch);
                }
            }

            _randomizeBtn.DrawOverlay(_geometryBatch);
            _createButton.DrawOverlay(_geometryBatch);
            if (_roster.Count > 0)
            {
                _backToRosterButton.DrawOverlay(_geometryBatch);
            }
        }
        else
        {
            for (int i = _roster.Count; i < CharacterRoster.MaxHeroes; i++)
            {
                // An empty slot invites a new hero: a "+" where the portrait would be.
                RectangleF b = _slotBounds[i];
                Vector2 c = new(b.X + 36f, b.Y + 38f);
                ColorRgba plus = i == _hoveredSlot ? CarbonUiPalette.BorderActive : MenuPalette.Icon;
                _geometryBatch.FillRectangle(new RectangleF(c.X - 9f, c.Y - 1.5f, 18f, 3f), plus);
                _geometryBatch.FillRectangle(new RectangleF(c.X - 1.5f, c.Y - 9f, 3f, 18f), plus);
            }

            if (_roster.HasFreeSlot)
            {
                _openCreatorButton.DrawOverlay(_geometryBatch);
            }

            _enterWorldButton.DrawOverlay(_geometryBatch);
        }

        _turnLeftBtn.DrawOverlay(_geometryBatch);
        _turnRightBtn.DrawOverlay(_geometryBatch);
        _logoutButton.DrawOverlay(_geometryBatch);

        if (!_isCreatorMode && _vectorArt is not null)
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
        spriteBatch.DrawOutlinedString(font, header, new Vector2(ox + 22f, oy + 18f), CarbonUiPalette.TextWarm, black, 2, 1.5f);
        // Right-aligned against Log out so a longer translation never runs under the button.
        string connection = _netText.Format("character.connection", new TextArg("status", _session.ConnectionLabel));
        TextMeasureResult connectionSize = font.MeasureString(connection, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, connection, new Vector2(MathF.Floor(_logoutButton.Bounds.X - 14f - connectionSize.Width), oy + 22f), CarbonUiPalette.TextMuted, black, 1, 1f);

        string stageTitle = GameText.Get("character.stage");
        TextMeasureResult stSize = font.MeasureString(stageTitle, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, stageTitle, new Vector2(MathF.Floor(stageCard.X + ((stageCard.Width - stSize.Width) * 0.5f)), stageCard.Y + 14f), CarbonUiPalette.TextPrimary, black, 1, 1f);

        if (_isCreatorMode)
        {
            // A slimmer lore strip under the name: the fresh-start note (ADR-030) in small type.
            spriteBatch.DrawOutlinedString(font, GameText.Get("character_creator.fresh_start"), new Vector2(_loreStrip.X + 12f, _loreStrip.Y + 10f), CarbonUiPalette.TextWarm, black, 1, 1f);
            for (int line = 0; line < s_freshStartLines.Length; line++)
            {
                string text = CarbonUiSkin.TruncateToWidth(font, GameText.Get(s_freshStartLines[line]), _loreStrip.Width - 24f, 1f);
                spriteBatch.DrawOutlinedString(font, text, new Vector2(_loreStrip.X + 12f, _loreStrip.Y + 34f + (line * 21f)), CarbonUiPalette.TextMuted, black, 1, 1f);
            }

            spriteBatch.DrawOutlinedString(font, GameText.Get("character.appearance"), new Vector2(_appearanceCard.X + 16f, _appearanceCard.Y + 14f), CarbonUiPalette.TextWarm, black, 1, 1f);
            this.SyncRows();
            for (int row = 0; row < this.ShownOptionRows; row++)
            {
                if (row == BodyRow && this.BodyUsesToggle)
                {
                    _bodyToggle.DrawText(spriteBatch, font);
                }
                else
                {
                    _rows[row].DrawText(spriteBatch, font);
                }
            }

            _randomizeBtn.DrawText(spriteBatch, font);
            _createNameField.DrawText(spriteBatch, font);
            _createButton.DrawText(spriteBatch, font);
            if (_roster.Count > 0)
            {
                _backToRosterButton.DrawText(spriteBatch, font);
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
                    spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, c.Name, b.Width - 82f, 1.25f), new Vector2(b.X + 70f, b.Y + 14f), nc, black, 1, 1.25f);
                    spriteBatch.DrawOutlinedString(font, _rosterRowText[i].Format("character.roster_row", new TextArg("class", clsLabel), new TextArg("level", c.Level)), new Vector2(b.X + 70f, b.Y + 42f), CarbonUiPalette.TextMuted, black, 1, 1f);
                }
                else
                {
                    spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, GameText.Get("character.empty_slot"), b.Width - 82f, 1f), new Vector2(b.X + 70f, b.Y + 30f), i == _hoveredSlot ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextMuted, black, 1, 1f);
                }
            }

            spriteBatch.DrawOutlinedString(font, GameText.Get("character.dossier"), new Vector2(rightCard.X + 16f, rightCard.Y + 14f), CarbonUiPalette.TextWarm, black, 1, 1f);
            if (_roster.HasSelection)
            {
                CharacterRosterEntry sel = _roster.Heroes[_roster.SelectedIndex];
                string selCls = GameText.ClassName(sel.Class);
                spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, sel.Name.ToUpperInvariant(), rightCard.Width - 32f, 1.5f), new Vector2(rightCard.X + 16f, rightCard.Y + 44f), CarbonUiPalette.TextPrimary, black, 1, 1.5f);
                spriteBatch.DrawOutlinedString(font, _dossierText.Format("character.dossier_line", new TextArg("class", selCls), new TextArg("level", sel.Level), new TextArg("tier", sel.ZoneTier)), new Vector2(rightCard.X + 16f, rightCard.Y + 78f), CarbonUiPalette.TextWarm, black, 1, 1f);
                spriteBatch.DrawOutlinedString(font, _positionText.Format("character.position", new TextArg("x", (int)sel.PositionX), new TextArg("y", (int)sel.PositionY)), new Vector2(rightCard.X + 16f, rightCard.Y + 102f), CarbonUiPalette.TextMuted, black, 1, 1f);
            }

            if (_roster.HasFreeSlot)
            {
                _openCreatorButton.DrawText(spriteBatch, font);
            }

            _enterWorldButton.DrawText(spriteBatch, font);
        }

        _turnLeftBtn.DrawText(spriteBatch, font);
        _turnRightBtn.DrawText(spriteBatch, font);
        _logoutButton.DrawText(spriteBatch, font);

        ColorRgba statusColor = _requests.StatusIsError ? MenuPalette.ErrorText : CarbonUiPalette.TextPrimary;
        string status = CarbonUiSkin.TruncateToWidth(font, _requests.StatusMessage, statusPill.Width - 20f, 1f);
        spriteBatch.DrawOutlinedString(font, status, new Vector2(statusPill.X + 10f, statusPill.Y + 7f), statusColor, black, 1, 1f);

        string footer = CarbonUiSkin.TruncateToWidth(font, GameText.Get(_isCreatorMode ? "character.footer.creator" : "character.footer.roster"), width - 32f, 1f);
        TextMeasureResult footerSize = font.MeasureString(footer, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, footer, new Vector2(MathF.Floor((width - footerSize.Width) * 0.5f), height - 30f), CarbonUiPalette.TextMuted, black, 1, 1f);
        spriteBatch.End();

        base.Render(context, spriteBatch, dt);

        _capture.AfterRender(context, ready: !_requests.IsLoadingRoster && !_requests.IsCreating && _flow.Backdrop.IsSettled(expectWorld: true));
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
            _fonts?.Dispose();
            _fonts = null;
        }

        base.Dispose(disposing);
    }

    private void UpdateRosterMode(KeyboardSnapshot kb, Vector2 mousePos, bool leftDown, bool leftPressed, bool busy)
    {
        _createNameField.IsFocused = false;
        _hoveredSlot = -1;
        for (int i = 0; i < CharacterRoster.MaxHeroes; i++)
        {
            if (_slotBounds[i].Contains(mousePos))
            {
                _hoveredSlot = i;
            }
        }

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

        if (_roster.HasFreeSlot && _openCreatorButton.Update(mousePos, leftDown, leftPressed, _frameDt))
        {
            _isCreatorMode = true;
            _createNameField.IsFocused = true;
            return;
        }

        bool enterKey = !busy && (kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter));
        if ((_enterWorldButton.Update(mousePos, leftDown, leftPressed, _frameDt) || enterKey) && hasSelection)
        {
            _requests.EnterWorld(_roster.Selected);
        }
    }

    private void UpdateCreatorMode(KeyboardSnapshot kb, Vector2 mousePos, float scrollY, bool leftDown, bool leftPressed, float dt, bool busy)
    {
        _createNameField.Update(dt, mousePos, leftPressed && !busy);
        this.SyncRows();
        for (int row = 0; row < VisibleOptionRows; row++)
        {
            _rows[row].IsEnabled = !busy && this.RowChoiceCount(row) > 1;
            _rows[row].IsFocused = _pickerFocus == row;
        }

        _bodyToggle.IsEnabled = !busy;
        _bodyToggle.IsFocused = _pickerFocus == BodyRow;

        _randomizeBtn.IsEnabled = !busy;
        _createButton.IsEnabled = !busy && _roster.HasFreeSlot;
        _backToRosterButton.IsEnabled = !busy && _roster.Count > 0;

        if (scrollY != 0f && _optionsBounds.Contains(mousePos))
        {
            this.ScrollOptions(scrollY > 0f ? -1 : 1);
        }

        if (kb.IsKeyPressed(Key.Tab) && this.ShownOptionRows > 0)
        {
            // Tab walks the selector rows then the name: rows 0..n-1, then -1 (the name field).
            bool back = kb.IsKeyDown(Key.ShiftLeft) || kb.IsKeyDown(Key.ShiftRight);
            int count = this.ShownOptionRows + 1;
            int at = _pickerFocus < 0 ? this.ShownOptionRows : _pickerFocus;
            at = (((at + (back ? -1 : 1)) % count) + count) % count;
            _pickerFocus = at == this.ShownOptionRows ? -1 : at;
            _createNameField.IsFocused = _pickerFocus < 0;
        }

        if (leftPressed && _createNameField.Bounds.Contains(mousePos))
        {
            _pickerFocus = -1;
        }

        for (int row = 0; row < this.ShownOptionRows; row++)
        {
            if (row == BodyRow && this.BodyUsesToggle)
            {
                // Two bodies: a toggle; choosing the other half is one step through the two.
                if (_bodyToggle.Update(mousePos, leftPressed, dt))
                {
                    this.CycleRow(BodyRow, 1);
                }

                continue;
            }

            int step = _rows[row].Update(mousePos, leftDown, leftPressed, dt);
            if (step != 0)
            {
                this.CycleRow(row, step);
            }
        }

        if (_randomizeBtn.Update(mousePos, leftDown, leftPressed, _frameDt))
        {
            _appearance.Randomize(Random.Shared);
        }

        if (_roster.Count > 0 && _backToRosterButton.Update(mousePos, leftDown, leftPressed, _frameDt))
        {
            _isCreatorMode = false;
            _createNameField.IsFocused = false;
            return;
        }

        bool enterKey = !busy && (kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter));
        if (_createButton.Update(mousePos, leftDown, leftPressed, _frameDt) || (enterKey && _createNameField.IsFocused && _roster.HasFreeSlot))
        {
            _createAttempted = true;
            if (AccountRules.HeroNameError(_createNameField.Text) is null)
            {
                _requests.Create(_createNameField.Text, _appearance.Look);
            }
            else
            {
                _createNameField.IsFocused = true;
            }
        }

        this.RefreshNameFeedback();
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
                _enteringWorld = true;
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

    /// <summary>
    /// Resolves the UI scale for the window (re-baking the font only when it changes) and the canvas it leaves.
    /// </summary>
    private void ApplyScale(int windowWidth, int windowHeight)
    {
        float scale = Rendering.UiSkin.UiScale.Resolve(_uiScaleChoice, windowWidth, windowHeight);
        this.UiScale = scale;
        _canvasWidth = (int)MathF.Floor(windowWidth / scale);
        _canvasHeight = (int)MathF.Floor(windowHeight / scale);
        _ = _fonts?.Get(scale);

        if (_uiSkin is { } skin)
        {
            skin.PixelScale = scale;
        }
    }

    private void LayoutControls(int width, int height)
    {
        const float totalW = 920f;
        const float totalH = 580f;
        float ox = MathF.Floor((width - totalW) * 0.5f);
        float oy = MathF.Floor((height - totalH) * 0.5f) + 10f;
        this.PanelBounds = new RectangleF(ox, oy, totalW, totalH);

        _logoutButton.Bounds = new RectangleF(ox + totalW - 122f, oy + 14f, 100f, 34f);
        _turnLeftBtn.Bounds = new RectangleF(ox + 324f, oy + 472f, 112f, 32f);
        _turnRightBtn.Bounds = new RectangleF(ox + 448f, oy + 472f, 112f, 32f);

        for (int i = 0; i < CharacterRoster.MaxHeroes; i++)
        {
            _slotBounds[i] = new RectangleF(ox + 22f, oy + 68f + (i * 84f), 270f, 76f);
        }

        _openCreatorButton.Bounds = new RectangleF(ox + 22f, oy + 476f, 270f, 44f);
        _enterWorldButton.Bounds = new RectangleF(ox + 608f, oy + 460f, 274f, 46f);

        // Creator: selector rows on the left (race, body type, the race's options; room to spare), the hero in the
        // middle, the name, Create and a slim lore strip on the right (docs/art/menu-ui.md rule 18).
        _appearanceCard = new RectangleF(ox + 22f, oy + 68f, 270f, 452f);
        for (int row = 0; row < VisibleOptionRows; row++)
        {
            _rows[row].Bounds = new RectangleF(_appearanceCard.X + 16f, _appearanceCard.Y + 44f + (row * SelectorRowStep), _appearanceCard.Width - 32f, 28f);
        }

        _bodyToggle.Bounds = _rows[BodyRow].Bounds;
        _optionsBounds = new RectangleF(_appearanceCard.X + 16f, _appearanceCard.Y + 44f + (FirstOptionRow * SelectorRowStep), _appearanceCard.Width - 32f, VisibleAppearanceRows * SelectorRowStep);
        _randomizeBtn.Bounds = new RectangleF(_appearanceCard.X + 16f, _appearanceCard.Bottom - 48f, _appearanceCard.Width - 32f, 32f);

        float rx = ox + 592f;
        float ry = oy + 68f;
        _createNameField.Bounds = new RectangleF(rx + 16f, ry + 48f, 274f, 38f);
        if (_roster.Count > 0)
        {
            _createButton.Bounds = new RectangleF(rx + 16f, ry + 120f, 182f, 44f);
            _backToRosterButton.Bounds = new RectangleF(rx + 206f, ry + 120f, 84f, 44f);
        }
        else
        {
            _createButton.Bounds = new RectangleF(rx + 16f, ry + 120f, 274f, 44f);
        }

        _loreStrip = new RectangleF(rx + 16f, ry + 184f, 274f, 252f);
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
            if (!_createNameField.AppendChar(ch))
            {
                // Explain a refused key (a space, an accented letter) where the player is looking.
                _nameRejectedFor = 3f;
            }
        }
    }

    /// <summary>
    /// The line under the hero name: why a key was refused (for a few seconds), the rule after a failed create, or a
    /// check once the name is one the server will take.
    /// </summary>
    private void RefreshNameFeedback()
    {
        string? error = AccountRules.HeroNameError(_createNameField.Text);
        if (_nameRejectedFor > 0f)
        {
            _createNameField.SetFeedback(FieldFeedback.Error, GameText.Get("character.name.chars"));
        }
        else if (error is not null && _createAttempted)
        {
            _createNameField.SetFeedback(FieldFeedback.Error, GameText.Get(error));
        }
        else if (error is null)
        {
            _createNameField.SetFeedback(FieldFeedback.Success, GameText.Get("character.name.ok"));
        }
        else
        {
            _createNameField.SetFeedback(FieldFeedback.None, string.Empty);
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

    private static MenuPicker[] CreatePickers()
    {
        MenuPicker[] rows = new MenuPicker[VisibleOptionRows];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new MenuPicker(1);
        }

        return rows;
    }

    /// <summary>Gets whether the body row is a two-state toggle: the race has exactly two bodies.</summary>
    private bool BodyUsesToggle => _appearance.Race is { Bodies.Count: 2 };

    /// <summary>Shows the look in the selector rows: label and value, "n/N", and the body toggle's two names.</summary>
    private void SyncRows()
    {
        for (int row = 0; row < this.ShownOptionRows; row++)
        {
            MenuPicker picker = _rows[row];
            int choices = this.RowChoiceCount(row);
            picker.SetCount(Math.Max(1, choices));
            picker.Index = this.RowChoiceIndex(row);
            (string labelKey, string valueKey) = this.RowKeys(row);
            picker.Text = _optionRowText[row].Format("appearance.row", new TextArg("label", GameText.Get(labelKey)), new TextArg("value", GameText.Get(valueKey)));
            picker.IndexText = choices > 1
                ? _traitIndexText[row].Format("character.appearance.index", new TextArg("index", picker.Index + 1), new TextArg("count", choices))
                : string.Empty;
        }

        if (this.BodyUsesToggle)
        {
            RaceDefinition race = _appearance.Race!;
            _bodyToggle.First = GameText.Get(race.Bodies[0].NameKey);
            _bodyToggle.Second = GameText.Get(race.Bodies[1].NameKey);
            _bodyToggle.Selected = this.RowChoiceIndex(BodyRow);
        }
    }

    /// <summary>Gets the label and value keys of selector row <paramref name="row"/>, all from <c>races.json</c> (behaviour tests).</summary>
    internal (string LabelKey, string ValueKey) RowKeys(int row)
    {
        RaceDefinition race = _appearance.Race!;
        if (row == RaceRow)
        {
            return ("character_creator.race", race.NameKey);
        }

        if (row == BodyRow)
        {
            return ("character_creator.body", _appearance.Body!.NameKey);
        }

        int option = row - FirstOptionRow + _optionScroll;
        AppearanceOptionDefinition definition = race.Options[option];
        return (definition.NameKey, definition.Choices[_appearance.GetChoice(option)].NameKey);
    }

    /// <summary>How many values selector row <paramref name="row"/> cycles through (0 when the row is not shown).</summary>
    private int RowChoiceCount(int row)
    {
        RaceDefinition? race = _appearance.Race;
        if (race is null || row >= this.ShownOptionRows)
        {
            return 0;
        }

        return row switch
        {
            RaceRow => _appearance.Catalog!.Races.Count,
            BodyRow => race.Bodies.Count,
            _ => race.Options[row - FirstOptionRow + _optionScroll].Choices.Count,
        };
    }

    /// <summary>Where the current pick of selector row <paramref name="row"/> sits among its choices (0-based).</summary>
    private int RowChoiceIndex(int row)
    {
        RaceDefinition? race = _appearance.Race;
        if (race is null)
        {
            return 0;
        }

        switch (row)
        {
            case RaceRow:
                List<RaceDefinition> races = _appearance.Catalog!.Races;
                for (int i = 0; i < races.Count; i++)
                {
                    if (ReferenceEquals(races[i], race))
                    {
                        return i;
                    }
                }

                return 0;
            case BodyRow:
                for (int i = 0; i < race.Bodies.Count; i++)
                {
                    if (ReferenceEquals(race.Bodies[i], _appearance.Body))
                    {
                        return i;
                    }
                }

                return 0;
            default:
                return _appearance.GetChoice(row - FirstOptionRow + _optionScroll);
        }
    }

    private void CycleRow(int row, int step)
    {
        switch (row)
        {
            case RaceRow:
                _appearance.CycleRace(step);
                _optionScroll = 0;
                break;
            case BodyRow:
                _appearance.CycleBody(step);
                break;
            default:
                _appearance.CycleOption(row - FirstOptionRow + _optionScroll, step);
                break;
        }
    }

    private void ScrollOptions(int step) =>
        _optionScroll = Math.Clamp(_optionScroll + step, 0, Math.Max(0, _appearance.OptionCount - VisibleAppearanceRows));
}

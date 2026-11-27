// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Capture;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Camera;
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
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Carbon.Desktop.Rendering.Fonts;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// Two-mode Hero Roster &amp; Character Creator scene framed with standardized 9-slice <see cref="CarbonUiSkin"/>,
/// featuring 4-direction live hero turntable, class archetype selection (<c>Vanguard</c>/<c>Ranger</c>/<c>Arcanist</c>),
/// and appearance trait customization.
/// </summary>
public sealed class CharacterSelectScene : GameScene
{
    private static readonly string[] s_hairStyles = ["character.hair.crop", "character.hair.spiky", "character.hair.long", "character.hair.bob"];
    private static readonly string[] s_hairColors = ["character.hair_color.raven", "character.hair_color.brown", "character.hair_color.gold", "character.hair_color.silver"];
    private static readonly string[] s_faceStyles = ["character.eyes.cobalt", "character.eyes.crimson", "character.eyes.emerald", "character.eyes.mystic"];
    private static readonly string[] s_skinTones = ["character.skin.fair", "character.skin.tan", "character.skin.bronze", "character.skin.umber"];
    private static readonly Direction8[] s_turnDirs = [Direction8.South, Direction8.West, Direction8.North, Direction8.East];
    private static readonly uint[] s_classWeapons = [StarterGameCatalog.IronSword, StarterGameCatalog.HuntingBow, StarterGameCatalog.ArcaneStaff];
    private static readonly string[] s_classTitles = ["character.class.vanguard", "character.class.ranger", "character.class.arcanist"];
    private static readonly string[] s_classSubtitles =
    [
        "character.class_hint.vanguard",
        "character.class_hint.ranger",
        "character.class_hint.arcanist",
    ];

    private readonly IWorldSession _session;
    private readonly DesktopOptions _options;
    private readonly Camera2D _camera;

    private readonly MenuTextField _createNameField;
    private readonly MenuButton _enterWorldButton = new(GameText.Get("character.enter_world"), MenuButtonStyle.SecondaryEmerald);
    private readonly MenuButton _openCreatorButton = new(GameText.Get("character.new_hero"), MenuButtonStyle.PrimaryGold);
    private readonly MenuButton _backToRosterButton = new(GameText.Get("character.back"), MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _createButton = new(GameText.Get("character.create"), MenuButtonStyle.PrimaryGold);
    private readonly MenuButton _turnLeftBtn = new(GameText.Get("character.turn_left"), MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _turnRightBtn = new(GameText.Get("character.turn_right"), MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _randomizeBtn = new(GameText.Get("character.randomize"), MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _hairPrevBtn = new("<", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _hairNextBtn = new(">", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _hColPrevBtn = new("<", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _hColNextBtn = new(">", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _facePrevBtn = new("<", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _faceNextBtn = new(">", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _skinPrevBtn = new("<", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _skinNextBtn = new(">", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _logoutButton = new(GameText.Get("character.logout"), MenuButtonStyle.DangerRed);
    private readonly RectangleF[] _slotBounds = new RectangleF[4];
    private readonly RectangleF[] _classCardBounds = new RectangleF[3];

    private GeometryBatch? _geometryBatch;
    private BitmapFont? _font;
    private Texture2D? _whitePixel;
    private CarbonUiSkin? _uiSkin;
    private IVectorArtRepository? _vectorArt;
    private IInputManager? _boundInput;

    private IReadOnlyList<CharacterRosterEntry> _characters = [];
    private int _selectedIndex;
    private int _turnIndex;
    private byte _hairStyle;
    private byte _hairColor;
    private byte _faceStyle;
    private byte _skinTone;
    private byte _selectedPreset;
    private HeroArchetypeClass _selectedClass = HeroArchetypeClass.Vanguard;
    private bool _isCreatorMode;
    private string? _preferSelectName;

    private Task<IReadOnlyList<CharacterRosterEntry>>? _listTask;
    private Task<(bool Succeeded, string Message)>? _createTask;
    private Task<(bool Succeeded, string Message)>? _enterTask;
    private Task? _logoutTask;

    private float _elapsed;
    private float _backspaceHoldTime;
    private float _backspaceRepeatTime;
    private readonly MenuSceneCapture _capture;
    private readonly CachedText _headerText = new();
    private readonly CachedText _netText = new();
    private readonly CachedText _hairText = new();
    private readonly CachedText _toneText = new();
    private readonly CachedText _eyesText = new();
    private readonly CachedText _skinText = new();
    private readonly CachedText _dossierText = new();
    private readonly CachedText _positionText = new();
    private readonly CachedText[] _rosterRowText = [new(), new(), new(), new()];
    private string _statusMessage = GameText.Get("character.status.loading");
    private bool _statusIsError;

    /// <summary>
    /// Initializes a new instance of the <see cref="CharacterSelectScene"/> class.
    /// </summary>
    public CharacterSelectScene(IWorldSession session, DesktopOptions options, Camera2D camera)
        : base("CharacterSelect")
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _capture = new MenuSceneCapture(options, "characters");

        _createNameField = new MenuTextField(GameText.Get("character.field.name"), GameText.Get("character.hint.name"), options.CharacterName, isPassword: false, maxLength: 20)
        {
            CharacterFilter = static ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == ' ',
        };
    }

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
        _listTask = _session.ListCharactersAsync();
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
        this.PollAsyncTasks(context);

        if (_logoutTask is not null || _enterTask is not null)
        {
            return;
        }

        int width = context.WindowWidth;
        int height = context.WindowHeight;
        this.LayoutControls(width, height);

        KeyboardSnapshot kb = context.Input.Keyboard;
        MouseSnapshot mouse = context.Input.Mouse;
        bool busy = _listTask is not null || _createTask is not null;

        if (kb.IsKeyPressed(Key.Escape))
        {
            if (_createNameField.IsFocused)
            {
                _createNameField.IsFocused = false;
            }
            else if (_isCreatorMode && _characters.Count > 0)
            {
                _isCreatorMode = false;
            }
            else
            {
                this.BeginLogout();
                return;
            }
        }

        if (!_isCreatorMode && _characters.Count > 0)
        {
            if (kb.IsKeyPressed(Key.Up))
            {
                _selectedIndex = (_selectedIndex + _characters.Count - 1) % _characters.Count;
            }
            else if (kb.IsKeyPressed(Key.Down))
            {
                _selectedIndex = (_selectedIndex + 1) % _characters.Count;
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
            for (int c = 0; c < 3; c++)
            {
                bool sel = (byte)_selectedClass == c;
                ColorRgba border = sel ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle;
                ColorRgba body = sel ? CarbonUiPalette.SlotSelectedFill : CarbonUiPalette.SlotFill;
                _uiSkin.DrawPanel(spriteBatch, UiPanelStyle.DoubleCard, in _classCardBounds[c], body, border);
            }

            _hairPrevBtn.DrawSkin(spriteBatch, _uiSkin);
            _hairNextBtn.DrawSkin(spriteBatch, _uiSkin);
            _hColPrevBtn.DrawSkin(spriteBatch, _uiSkin);
            _hColNextBtn.DrawSkin(spriteBatch, _uiSkin);
            _facePrevBtn.DrawSkin(spriteBatch, _uiSkin);
            _faceNextBtn.DrawSkin(spriteBatch, _uiSkin);
            _skinPrevBtn.DrawSkin(spriteBatch, _uiSkin);
            _skinNextBtn.DrawSkin(spriteBatch, _uiSkin);
            _randomizeBtn.DrawSkin(spriteBatch, _uiSkin);
            _createNameField.DrawSkin(spriteBatch, _uiSkin);
            _createButton.DrawSkin(spriteBatch, _uiSkin);
            if (_characters.Count > 0)
            {
                _backToRosterButton.DrawSkin(spriteBatch, _uiSkin);
            }
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                bool occupied = i < _characters.Count;
                bool sel = occupied && i == _selectedIndex;
                ColorRgba border = sel ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle;
                ColorRgba body = sel ? CarbonUiPalette.SlotSelectedFill : new ColorRgba(CarbonUiPalette.SlotFill.R, CarbonUiPalette.SlotFill.G, CarbonUiPalette.SlotFill.B, occupied ? (byte)234 : (byte)175);
                _uiSkin.DrawPanel(spriteBatch, UiPanelStyle.DoubleCard, in _slotBounds[i], body, border);
                if (occupied)
                {
                    RectangleF pBox = new(_slotBounds[i].X + 10f, _slotBounds[i].Y + 12f, 52f, 52f);
                    _uiSkin.DrawSlot(spriteBatch, in pBox, CarbonUiPalette.SlotFill, sel ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderOuter);
                }
            }

            if (_characters.Count < 4)
            {
                _openCreatorButton.DrawSkin(spriteBatch, _uiSkin);
            }

            _enterWorldButton.DrawSkin(spriteBatch, _uiSkin);
        }

        _turnLeftBtn.DrawSkin(spriteBatch, _uiSkin);
        _turnRightBtn.DrawSkin(spriteBatch, _uiSkin);
        _logoutButton.DrawSkin(spriteBatch, _uiSkin);
        _uiSkin.DrawInset(spriteBatch, in statusPill, CarbonUiPalette.SlotFill, _statusIsError ? CarbonUiPalette.DangerBorder : CarbonUiPalette.BorderSubtle);
        spriteBatch.End();

        // Pass 3: Live Vector Hero Pedestal, Class Weapon Badges, Portraits & Cursor
        _geometryBatch.Begin(proj);
        Direction8 previewDir = s_turnDirs[_turnIndex];
        float stageCx = stageCard.X + (stageCard.Width * 0.5f);
        float stageCy = stageCard.Y + 240f;
        _geometryBatch.FillCircle(new Vector2(stageCx, stageCy), 44f, new ColorRgba(6, 10, 18, 145), 22);
        _geometryBatch.DrawCircle(new Vector2(stageCx, stageCy), 45f, CarbonUiPalette.BorderOuter, 22, 1.8f);

        byte previewPreset = _isCreatorMode || _selectedIndex < 0 || _selectedIndex >= _characters.Count
            ? _selectedPreset
            : _characters[_selectedIndex].AppearancePreset;
        if (_vectorArt is not null && _vectorArt.TryGetCharacter(previewPreset, out CompiledVectorRig? stageRig))
        {
            CompiledItemVectorArt? weaponArt = null;
            VisualEquipmentLoadout previewLoadout = default;
            HeroArchetypeClass previewClass = _isCreatorMode
                ? _selectedClass
                : (_selectedIndex >= 0 && _selectedIndex < _characters.Count
                    ? _characters[_selectedIndex].Class
                    : HeroArchetypeClass.Vanguard);
            if (_isCreatorMode || (_selectedIndex >= 0 && _selectedIndex < _characters.Count))
            {
                int classIdx = Math.Clamp((int)previewClass, 0, s_classWeapons.Length - 1);
                uint starterWeaponId = s_classWeapons[classIdx];
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
                appearancePreset: previewPreset,
                equipment: previewLoadout,
                art: _vectorArt);
        }

        if (_isCreatorMode)
        {
            if (_vectorArt is not null)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (_vectorArt.TryGetItem(s_classWeapons[c], out CompiledItemVectorArt? wArt))
                    {
                        VectorRigRenderer.DrawItemIcon(_geometryBatch, wArt, new Vector2(_classCardBounds[c].X + 34f, _classCardBounds[c].Y + 46f), 34f);
                    }
                }
            }

            _createNameField.DrawCursorGeometry(_geometryBatch, _font);
        }
        else if (_vectorArt is not null)
        {
            for (int i = 0; i < 4 && i < _characters.Count; i++)
            {
                if (_vectorArt.TryGetCharacter(_characters[i].AppearancePreset, out CompiledVectorRig? cardRig))
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
                        appearancePreset: _characters[i].AppearancePreset,
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
            : _headerText.Format("character.header.roster", new TextArg("count", _characters.Count), new TextArg("max", 4));
        spriteBatch.DrawOutlinedString(_font, header, new Vector2(ox + 22f, oy + 18f), CarbonUiPalette.TextWarm, black, 2, 1.5f);
        spriteBatch.DrawOutlinedString(_font, _netText.Format("character.net", new TextArg("transport", _session.TransportLabel)), new Vector2(ox + 686f, oy + 22f), CarbonUiPalette.TextMuted, black, 1, 1f);

        string stageTitle = _isCreatorMode ? GameText.Get(s_classTitles[(int)_selectedClass]) : GameText.Get("character.stage");
        TextMeasureResult stSize = _font.MeasureString(stageTitle, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(_font, stageTitle, new Vector2(MathF.Floor(stageCard.X + ((stageCard.Width - stSize.Width) * 0.5f)), stageCard.Y + 14f), CarbonUiPalette.TextPrimary, black, 1, 1f);

        if (_isCreatorMode)
        {
            spriteBatch.DrawOutlinedString(_font, GameText.Get("character.starting_class"), new Vector2(ox + 24f, oy + 66f), CarbonUiPalette.TextMuted, black, 1, 1f);
            for (int c = 0; c < 3; c++)
            {
                RectangleF cb = _classCardBounds[c];
                ColorRgba accent = (byte)_selectedClass == c ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextPrimary;
                spriteBatch.DrawOutlinedString(_font, GameText.Get(s_classTitles[c]), new Vector2(cb.X + 64f, cb.Y + 18f), accent, black, 1, 1.25f);
                spriteBatch.DrawOutlinedString(_font, CarbonUiSkin.TruncateToWidth(_font, GameText.Get(s_classSubtitles[c]), cb.Width - 72f, 1f), new Vector2(cb.X + 64f, cb.Y + 48f), CarbonUiPalette.TextMuted, black, 1, 1f);
            }

            spriteBatch.DrawOutlinedString(_font, GameText.Get("character.appearance"), new Vector2(rightCard.X + 16f, rightCard.Y + 14f), CarbonUiPalette.TextWarm, black, 1, 1f);
            spriteBatch.DrawOutlinedString(_font, _hairText.Format("character.appearance.hair", new TextArg("value", GameText.Get(s_hairStyles[_hairStyle]))), new Vector2(rightCard.X + 52f, rightCard.Y + 49f), CarbonUiPalette.TextPrimary, black, 1, 1f);
            spriteBatch.DrawOutlinedString(_font, _toneText.Format("character.appearance.hair_color", new TextArg("value", GameText.Get(s_hairColors[_hairColor]))), new Vector2(rightCard.X + 52f, rightCard.Y + 83f), CarbonUiPalette.TextPrimary, black, 1, 1f);
            spriteBatch.DrawOutlinedString(_font, _eyesText.Format("character.appearance.eyes", new TextArg("value", GameText.Get(s_faceStyles[_faceStyle]))), new Vector2(rightCard.X + 52f, rightCard.Y + 117f), CarbonUiPalette.TextPrimary, black, 1, 1f);
            spriteBatch.DrawOutlinedString(_font, _skinText.Format("character.appearance.skin", new TextArg("value", GameText.Get(s_skinTones[_skinTone]))), new Vector2(rightCard.X + 52f, rightCard.Y + 151f), CarbonUiPalette.TextPrimary, black, 1, 1f);

            _hairPrevBtn.DrawText(spriteBatch, _font);
            _hairNextBtn.DrawText(spriteBatch, _font);
            _hColPrevBtn.DrawText(spriteBatch, _font);
            _hColNextBtn.DrawText(spriteBatch, _font);
            _facePrevBtn.DrawText(spriteBatch, _font);
            _faceNextBtn.DrawText(spriteBatch, _font);
            _skinPrevBtn.DrawText(spriteBatch, _font);
            _skinNextBtn.DrawText(spriteBatch, _font);
            _randomizeBtn.DrawText(spriteBatch, _font);
            _createNameField.DrawText(spriteBatch, _font);
            _createButton.DrawText(spriteBatch, _font);
            if (_characters.Count > 0)
            {
                _backToRosterButton.DrawText(spriteBatch, _font);
            }
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                RectangleF b = _slotBounds[i];
                if (i < _characters.Count)
                {
                    CharacterRosterEntry c = _characters[i];
                    ColorRgba nc = i == _selectedIndex ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextPrimary;
                    string clsLabel = GameText.Get(s_classTitles[Math.Clamp((int)c.Class, 0, s_classTitles.Length - 1)]);
                    spriteBatch.DrawOutlinedString(_font, CarbonUiSkin.TruncateToWidth(_font, c.Name, b.Width - 82f, 1.25f), new Vector2(b.X + 70f, b.Y + 14f), nc, black, 1, 1.25f);
                    spriteBatch.DrawOutlinedString(_font, _rosterRowText[i].Format("character.roster_row", new TextArg("class", clsLabel), new TextArg("level", c.Level)), new Vector2(b.X + 70f, b.Y + 42f), CarbonUiPalette.TextMuted, black, 1, 1f);
                }
                else
                {
                    spriteBatch.DrawOutlinedString(_font, GameText.Get("character.empty_slot"), new Vector2(b.X + 26f, b.Y + 30f), CarbonUiPalette.TextMuted, black, 1, 1f);
                }
            }

            spriteBatch.DrawOutlinedString(_font, GameText.Get("character.dossier"), new Vector2(rightCard.X + 16f, rightCard.Y + 14f), CarbonUiPalette.TextWarm, black, 1, 1f);
            if (_selectedIndex >= 0 && _selectedIndex < _characters.Count)
            {
                CharacterRosterEntry sel = _characters[_selectedIndex];
                string selCls = GameText.Get(s_classTitles[Math.Clamp((int)sel.Class, 0, s_classTitles.Length - 1)]);
                spriteBatch.DrawOutlinedString(_font, CarbonUiSkin.TruncateToWidth(_font, sel.Name.ToUpperInvariant(), rightCard.Width - 32f, 1.5f), new Vector2(rightCard.X + 16f, rightCard.Y + 44f), CarbonUiPalette.TextPrimary, black, 1, 1.5f);
                spriteBatch.DrawOutlinedString(_font, _dossierText.Format("character.dossier_line", new TextArg("class", selCls), new TextArg("level", sel.Level), new TextArg("tier", sel.ZoneTier)), new Vector2(rightCard.X + 16f, rightCard.Y + 78f), CarbonUiPalette.TextWarm, black, 1, 1f);
                spriteBatch.DrawOutlinedString(_font, _positionText.Format("character.position", new TextArg("x", (int)sel.PositionX), new TextArg("y", (int)sel.PositionY)), new Vector2(rightCard.X + 16f, rightCard.Y + 102f), CarbonUiPalette.TextMuted, black, 1, 1f);
            }

            if (_characters.Count < 4)
            {
                _openCreatorButton.DrawText(spriteBatch, _font);
            }

            _enterWorldButton.DrawText(spriteBatch, _font);
        }

        _turnLeftBtn.DrawText(spriteBatch, _font);
        _turnRightBtn.DrawText(spriteBatch, _font);
        _logoutButton.DrawText(spriteBatch, _font);

        ColorRgba statusColor = _statusIsError
            ? new ColorRgba(235, 130, 130, 255)
            : CarbonUiPalette.TextPrimary;
        string status = CarbonUiSkin.TruncateToWidth(_font, _statusMessage, statusPill.Width - 20f, 1f);
        spriteBatch.DrawOutlinedString(_font, status, new Vector2(statusPill.X + 10f, statusPill.Y + 7f), statusColor, black, 1, 1f);
        spriteBatch.End();

        base.Render(context, spriteBatch, dt);

        _capture.AfterRender(context, ready: _listTask is null && _createTask is null);
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
            for (int i = 0; i < 4; i++)
            {
                if (_slotBounds[i].Contains(mousePos))
                {
                    if (i < _characters.Count)
                    {
                        _selectedIndex = i;
                    }
                    else if (_characters.Count < 4)
                    {
                        _isCreatorMode = true;
                        _createNameField.IsFocused = true;
                        return;
                    }
                }
            }
        }

        bool hasSelection = _selectedIndex >= 0 && _selectedIndex < _characters.Count;
        _enterWorldButton.IsEnabled = !busy && hasSelection;
        _openCreatorButton.IsEnabled = !busy && _characters.Count < 4;

        if (_characters.Count < 4 && _openCreatorButton.Update(mousePos, leftDown, leftPressed))
        {
            _isCreatorMode = true;
            _createNameField.IsFocused = true;
            return;
        }

        bool enterKey = !busy && (kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter));
        if ((_enterWorldButton.Update(mousePos, leftDown, leftPressed) || enterKey) && hasSelection)
        {
            CharacterRosterEntry hero = _characters[_selectedIndex];
            _enterTask = _session.EnterWorldAsync(hero.CharacterId);
            _statusIsError = false;
            _statusMessage = GameText.Format("character.status.entering", new TextArg("name", hero.Name));
        }
    }

    private void UpdateCreatorMode(KeyboardSnapshot kb, Vector2 mousePos, bool leftDown, bool leftPressed, float dt, bool busy)
    {
        _createNameField.Update(dt, mousePos, leftPressed && !busy);
        if (leftPressed && !busy)
        {
            for (int c = 0; c < 3; c++)
            {
                if (_classCardBounds[c].Contains(mousePos))
                {
                    _selectedClass = (HeroArchetypeClass)c;
                }
            }
        }

        _hairPrevBtn.IsEnabled = !busy;
        _hairNextBtn.IsEnabled = !busy;
        _hColPrevBtn.IsEnabled = !busy;
        _hColNextBtn.IsEnabled = !busy;
        _facePrevBtn.IsEnabled = !busy;
        _faceNextBtn.IsEnabled = !busy;
        _skinPrevBtn.IsEnabled = !busy;
        _skinNextBtn.IsEnabled = !busy;
        _randomizeBtn.IsEnabled = !busy;
        _createButton.IsEnabled = !busy && _characters.Count < 4;
        _backToRosterButton.IsEnabled = !busy && _characters.Count > 0;

        if (_hairPrevBtn.Update(mousePos, leftDown, leftPressed))
        {
            _hairStyle = (byte)((_hairStyle + 3) % 4);
        }
        else if (_hairNextBtn.Update(mousePos, leftDown, leftPressed))
        {
            _hairStyle = (byte)((_hairStyle + 1) % 4);
        }

        if (_hColPrevBtn.Update(mousePos, leftDown, leftPressed))
        {
            _hairColor = (byte)((_hairColor + 3) % 4);
        }
        else if (_hColNextBtn.Update(mousePos, leftDown, leftPressed))
        {
            _hairColor = (byte)((_hairColor + 1) % 4);
        }

        if (_facePrevBtn.Update(mousePos, leftDown, leftPressed))
        {
            _faceStyle = (byte)((_faceStyle + 3) % 4);
        }
        else if (_faceNextBtn.Update(mousePos, leftDown, leftPressed))
        {
            _faceStyle = (byte)((_faceStyle + 1) % 4);
        }

        if (_skinPrevBtn.Update(mousePos, leftDown, leftPressed))
        {
            _skinTone = (byte)((_skinTone + 3) % 4);
        }
        else if (_skinNextBtn.Update(mousePos, leftDown, leftPressed))
        {
            _skinTone = (byte)((_skinTone + 1) % 4);
        }

        if (_randomizeBtn.Update(mousePos, leftDown, leftPressed))
        {
            _hairStyle = (byte)Random.Shared.Next(4);
            _hairColor = (byte)Random.Shared.Next(4);
            _faceStyle = (byte)Random.Shared.Next(4);
            _skinTone = (byte)Random.Shared.Next(4);
        }

        _selectedPreset = CharacterAppearance.Pack(_hairStyle, _hairColor, _faceStyle, _skinTone);

        if (_characters.Count > 0 && _backToRosterButton.Update(mousePos, leftDown, leftPressed))
        {
            _isCreatorMode = false;
            _createNameField.IsFocused = false;
            return;
        }

        bool enterKey = !busy && (kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter));
        if (_createButton.Update(mousePos, leftDown, leftPressed) || (enterKey && _createNameField.IsFocused && _characters.Count < 4))
        {
            _preferSelectName = _createNameField.Text.Trim();
            _createTask = _session.CreateCharacterAsync(_createNameField.Text, _selectedPreset, (byte)_selectedClass);
            _statusIsError = false;
            _statusMessage = GameText.Format("character.status.creating", new TextArg("name", _preferSelectName), new TextArg("class", GameText.Get(s_classTitles[(int)_selectedClass])));
        }
    }

    private void PollAsyncTasks(SceneContext context)
    {
        if (_logoutTask is not null && _logoutTask.IsCompleted)
        {
            Task done = _logoutTask;
            _logoutTask = null;
            try
            {
                done.GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Always return cleanly to LoginScene.
            }

            LoginScene? next = null;
            try
            {
                next = new LoginScene(_session, _options, _camera);
                context.SceneManager.ChangeScene(next, transitionDuration: 0.15f);
                next = null;
            }
            finally
            {
                next?.Dispose();
            }

            return;
        }

        if (_enterTask is not null && _enterTask.IsCompleted)
        {
            Task<(bool Succeeded, string Message)> done = _enterTask;
            _enterTask = null;
            try
            {
                (bool ok, string msg) = done.GetAwaiter().GetResult();
                if (ok)
                {
                    WorldGameplayScene? next = null;
                    try
                    {
                        next = new WorldGameplayScene(_session, _options, _camera);
                        context.SceneManager.ChangeScene(next, transitionDuration: 0.18f);
                        next = null;
                    }
                    finally
                    {
                        next?.Dispose();
                    }

                    return;
                }

                _statusIsError = true;
                _statusMessage = msg;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _statusIsError = true;
                _statusMessage = ex.Message;
            }
        }

        if (_createTask is not null && _createTask.IsCompleted)
        {
            Task<(bool Succeeded, string Message)> done = _createTask;
            _createTask = null;
            try
            {
                (bool ok, string msg) = done.GetAwaiter().GetResult();
                _statusIsError = !ok;
                _statusMessage = msg;
                if (ok)
                {
                    _isCreatorMode = false;
                    _createNameField.IsFocused = false;
                    _listTask = _session.ListCharactersAsync();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _statusIsError = true;
                _statusMessage = ex.Message;
            }
        }

        if (_listTask is not null && _listTask.IsCompleted)
        {
            Task<IReadOnlyList<CharacterRosterEntry>> done = _listTask;
            _listTask = null;
            try
            {
                _characters = done.GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(_preferSelectName))
                {
                    for (int i = 0; i < _characters.Count; i++)
                    {
                        if (string.Equals(_characters[i].Name, _preferSelectName, StringComparison.OrdinalIgnoreCase))
                        {
                            _selectedIndex = i;
                            break;
                        }
                    }

                    _preferSelectName = null;
                }
                else if (_selectedIndex >= _characters.Count)
                {
                    _selectedIndex = Math.Max(0, _characters.Count - 1);
                }

                if (_characters.Count == 0)
                {
                    _isCreatorMode = true;
                    _createNameField.IsFocused = true;
                }

                if (_createTask is null)
                {
                    _statusIsError = false;
                    _statusMessage = _characters.Count > 0
                        ? GameText.Plural("character.status.found", _characters.Count)
                        : GameText.Get("character.status.welcome");
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _statusIsError = true;
                _statusMessage = GameText.Format("character.status.load_failed", new TextArg("error", ex.Message));
            }
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

        for (int i = 0; i < 4; i++)
        {
            _slotBounds[i] = new RectangleF(ox + 22f, oy + 68f + (i * 84f), 270f, 76f);
        }

        _openCreatorButton.Bounds = new RectangleF(ox + 22f, oy + 408f, 270f, 44f);
        _enterWorldButton.Bounds = new RectangleF(ox + 608f, oy + 392f, 274f, 46f);

        for (int c = 0; c < 3; c++)
        {
            _classCardBounds[c] = new RectangleF(ox + 22f, oy + 92f + (c * 108f), 270f, 96f);
        }

        float rx = ox + 592f;
        float ry = oy + 68f;
        _hairPrevBtn.Bounds = new RectangleF(rx + 16f, ry + 44f, 28f, 26f);
        _hairNextBtn.Bounds = new RectangleF(rx + 262f, ry + 44f, 28f, 26f);
        _hColPrevBtn.Bounds = new RectangleF(rx + 16f, ry + 78f, 28f, 26f);
        _hColNextBtn.Bounds = new RectangleF(rx + 262f, ry + 78f, 28f, 26f);
        _facePrevBtn.Bounds = new RectangleF(rx + 16f, ry + 112f, 28f, 26f);
        _faceNextBtn.Bounds = new RectangleF(rx + 262f, ry + 112f, 28f, 26f);
        _skinPrevBtn.Bounds = new RectangleF(rx + 16f, ry + 146f, 28f, 26f);
        _skinNextBtn.Bounds = new RectangleF(rx + 262f, ry + 146f, 28f, 26f);
        _randomizeBtn.Bounds = new RectangleF(rx + 16f, ry + 184f, 274f, 32f);

        _createNameField.Bounds = new RectangleF(rx + 16f, ry + 250f, 274f, 36f);
        if (_characters.Count > 0)
        {
            _createButton.Bounds = new RectangleF(rx + 16f, ry + 302f, 182f, 44f);
            _backToRosterButton.Bounds = new RectangleF(rx + 206f, ry + 302f, 84f, 44f);
        }
        else
        {
            _createButton.Bounds = new RectangleF(rx + 16f, ry + 302f, 274f, 44f);
        }
    }

    private void BeginLogout()
    {
        _logoutTask = _session.LogoutAsync();
        _statusIsError = false;
        _statusMessage = GameText.Get("character.status.signing_out");
    }

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
        if (_isCreatorMode && _createTask is null && _enterTask is null && _logoutTask is null)
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
}

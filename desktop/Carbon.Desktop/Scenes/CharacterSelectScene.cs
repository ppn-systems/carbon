// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Textures;
using Carbon.Graphics.Scenes.Base;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Input;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// Interactive character selection and creation scene shown after online TCP authentication.
/// Enters the authoritative world and initializes Authenticated UDP upon selecting a hero.
/// </summary>
public sealed class CharacterSelectScene : GameScene
{
    private static readonly string[] s_hairStyles = ["CROP", "SPIKY", "LONG", "BOB"];
    private static readonly string[] s_hairColors = ["RAVEN", "BROWN", "GOLD", "SILVER"];
    private static readonly string[] s_faceStyles = ["COBALT", "CRIMSON", "EMERALD", "MYSTIC"];
    private static readonly string[] s_skinTones = ["FAIR", "TAN", "BRONZE", "UMBER"];

    private readonly IClientWorldSession _session;
    private readonly DesktopClientOptions _options;
    private readonly Camera2D _camera;

    private readonly MenuTextField _createNameField;
    private readonly MenuButton _enterWorldButton = new("ENTER WORLD", MenuButtonStyle.SecondaryEmerald);
    private readonly MenuButton _createButton = new("CREATE HERO", MenuButtonStyle.PrimaryGold);
    private readonly MenuButton _hairPrevBtn = new("<", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _hairNextBtn = new(">", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _hColPrevBtn = new("<", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _hColNextBtn = new(">", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _facePrevBtn = new("<", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _faceNextBtn = new(">", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _skinPrevBtn = new("<", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _skinNextBtn = new(">", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _logoutButton = new("LOGOUT", MenuButtonStyle.DangerRed);
    private readonly RectangleF[] _slotBounds = new RectangleF[4];

    private GeometryBatch? _geometryBatch;
    private BitmapFont? _font;
    private Texture2D? _whitePixel;
    private IVectorArtRepository? _vectorArt;
    private IInputManager? _boundInput;

    private IReadOnlyList<ClientCharacterSummary> _characters = [];
    private int _selectedIndex;
    private byte _hairStyle;
    private byte _hairColor;
    private byte _faceStyle;
    private byte _skinTone;
    private byte _selectedPreset;
    private bool _isCustomizingNewHero;
    private string? _preferSelectName;

    private Task<IReadOnlyList<ClientCharacterSummary>>? _listTask;
    private Task<(bool Succeeded, string Message)>? _createTask;
    private Task<(bool Succeeded, string Message)>? _enterTask;
    private Task? _logoutTask;

    private float _elapsed;
    private float _backspaceHoldTime;
    private float _backspaceRepeatTime;
    private string _statusMessage = "Loading characters from server...";
    private bool _statusIsError;

    /// <summary>
    /// Initializes a new instance of the <see cref="CharacterSelectScene"/> class.
    /// </summary>
    public CharacterSelectScene(IClientWorldSession session, DesktopClientOptions options, Camera2D camera)
        : base("CharacterSelect")
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));

        _createNameField = new MenuTextField("NEW CHARACTER NAME", "2-20 chars", options.CharacterName, isPassword: false, maxLength: 20)
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
        (_font, _whitePixel) = EmbeddedPixelFont.Create(context.Device);
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
            else
            {
                this.BeginLogout();
                return;
            }
        }

        if (kb.IsKeyPressed(Key.Up) && _characters.Count > 0)
        {
            _selectedIndex = (_selectedIndex + _characters.Count - 1) % _characters.Count;
            _isCustomizingNewHero = false;
        }
        else if (kb.IsKeyPressed(Key.Down) && _characters.Count > 0)
        {
            _selectedIndex = (_selectedIndex + 1) % _characters.Count;
            _isCustomizingNewHero = false;
        }

        this.HandleBackspaceRepeat(kb, (float)dt);

        bool leftDown = mouse.IsButtonDown(MouseButton.Left);
        bool leftPressed = mouse.IsButtonPressed(MouseButton.Left);
        Vector2 mousePos = mouse.Position;

        _createNameField.Update((float)dt, mousePos, leftPressed && !busy);
        if (_createNameField.IsFocused && leftPressed)
        {
            _isCustomizingNewHero = true;
        }

        if (leftPressed && !busy)
        {
            for (int i = 0; i < _slotBounds.Length; i++)
            {
                if (_slotBounds[i].Contains(mousePos) && i < _characters.Count)
                {
                    _selectedIndex = i;
                    _createNameField.IsFocused = false;
                    _isCustomizingNewHero = false;
                }
            }
        }

        bool hasSelection = _selectedIndex >= 0 && _selectedIndex < _characters.Count;
        _enterWorldButton.IsEnabled = !busy && hasSelection;
        _createButton.IsEnabled = !busy && _characters.Count < 4;
        _createButton.Text = _characters.Count >= 4 ? "ROSTER FULL (4/4)" : "CREATE HERO";
        _hairPrevBtn.IsEnabled = !busy;
        _hairNextBtn.IsEnabled = !busy;
        _hColPrevBtn.IsEnabled = !busy;
        _hColNextBtn.IsEnabled = !busy;
        _facePrevBtn.IsEnabled = !busy;
        _faceNextBtn.IsEnabled = !busy;
        _skinPrevBtn.IsEnabled = !busy;
        _skinNextBtn.IsEnabled = !busy;
        _logoutButton.IsEnabled = !busy;

        if (_logoutButton.Update(mousePos, leftDown, leftPressed))
        {
            this.BeginLogout();
            return;
        }

        bool changedCustom = false;
        if (_hairPrevBtn.Update(mousePos, leftDown, leftPressed))
        {
            _hairStyle = (byte)((_hairStyle + 3) % 4);
            changedCustom = true;
        }

        if (_hairNextBtn.Update(mousePos, leftDown, leftPressed))
        {
            _hairStyle = (byte)((_hairStyle + 1) % 4);
            changedCustom = true;
        }

        if (_hColPrevBtn.Update(mousePos, leftDown, leftPressed))
        {
            _hairColor = (byte)((_hairColor + 3) % 4);
            changedCustom = true;
        }

        if (_hColNextBtn.Update(mousePos, leftDown, leftPressed))
        {
            _hairColor = (byte)((_hairColor + 1) % 4);
            changedCustom = true;
        }

        if (_facePrevBtn.Update(mousePos, leftDown, leftPressed))
        {
            _faceStyle = (byte)((_faceStyle + 3) % 4);
            changedCustom = true;
        }

        if (_faceNextBtn.Update(mousePos, leftDown, leftPressed))
        {
            _faceStyle = (byte)((_faceStyle + 1) % 4);
            changedCustom = true;
        }

        if (_skinPrevBtn.Update(mousePos, leftDown, leftPressed))
        {
            _skinTone = (byte)((_skinTone + 3) % 4);
            changedCustom = true;
        }

        if (_skinNextBtn.Update(mousePos, leftDown, leftPressed))
        {
            _skinTone = (byte)((_skinTone + 1) % 4);
            changedCustom = true;
        }

        if (changedCustom)
        {
            _isCustomizingNewHero = true;
        }

        _selectedPreset = CharacterAppearance.Pack(_hairStyle, _hairColor, _faceStyle, _skinTone);

        bool enterKey = !busy && (kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter));
        if (_createButton.Update(mousePos, leftDown, leftPressed) || (enterKey && _createNameField.IsFocused && _characters.Count < 4))
        {
            _preferSelectName = _createNameField.Text.Trim();
            _createTask = _session.CreateCharacterAsync(_createNameField.Text, _selectedPreset);
            _statusIsError = false;
            _statusMessage = $"Creating {_preferSelectName}...";
        }
        else if (_enterWorldButton.Update(mousePos, leftDown, leftPressed) || (enterKey && !_createNameField.IsFocused && hasSelection))
        {
            ClientCharacterSummary hero = _characters[_selectedIndex];
            _enterTask = _session.EnterWorldAsync(hero.CharacterId);
            _statusIsError = false;
            _statusMessage = $"Entering world as {hero.Name} (opening Authenticated UDP)...";
        }
    }

    /// <inheritdoc/>
    public override void Render(SceneContext context, SpriteBatch spriteBatch, double dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(spriteBatch);

        if (_geometryBatch is null || _font is null)
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

        const float totalW = 860f;
        const float totalH = 480f;
        float ox = MathF.Floor((width - totalW) * 0.5f);
        float oy = MathF.Floor((height - totalH) * 0.5f) + 12f;

        _geometryBatch.Begin(proj);
        MenuBackdropRenderer.Draw(_geometryBatch, width, height, _elapsed);

        RectangleF mainCard = new(ox, oy, totalW, totalH);
        _geometryBatch.FillRectangle(mainCard, new ColorRgba(10, 15, 28, 232));
        _geometryBatch.DrawRectangle(mainCard, new ColorRgba(100, 116, 139, 225), 2f);
        _geometryBatch.FillRectangle(new RectangleF(ox, oy, totalW, 52f), new ColorRgba(15, 23, 42, 245));

        for (int i = 0; i < 4; i++)
        {
            bool occupied = i < _characters.Count;
            bool selected = occupied && i == _selectedIndex;
            ColorRgba cardFill = selected
                ? new ColorRgba(30, 58, 95, 240)
                : (occupied ? new ColorRgba(18, 28, 48, 230) : new ColorRgba(15, 23, 42, 165));
            ColorRgba cardBorder = selected
                ? new ColorRgba(250, 204, 21, 255)
                : new ColorRgba(71, 85, 105, 210);

            _geometryBatch.FillRectangle(_slotBounds[i], cardFill);
            _geometryBatch.DrawRectangle(_slotBounds[i], cardBorder, selected ? 2f : 1.5f);
        }

        // Live modular avatar preview pedestal
        byte previewPreset = !_isCustomizingNewHero && _selectedIndex >= 0 && _selectedIndex < _characters.Count
            ? _characters[_selectedIndex].AppearancePreset
            : _selectedPreset;
        float previewCx = ox + 516f;
        float previewCy = oy + 240f;
        _geometryBatch.FillCircle(new Vector2(previewCx, previewCy + 14f), 24f, new ColorRgba(0, 0, 0, 105), 16);
        _geometryBatch.DrawCircle(new Vector2(previewCx, previewCy + 14f), 25f, new ColorRgba(56, 189, 248, 225), 18, 1.5f);
        if (_vectorArt is not null && _vectorArt.TryGetCharacter(previewPreset, out CompiledVectorRig? previewRig))
        {
            VectorRigRenderer.DrawEntityRig(
                _geometryBatch,
                previewRig,
                new Vector2(previewCx, previewCy + 20f),
                Direction8.South,
                "idle",
                _elapsed,
                isNormalized: false,
                targetHeightOverride: 62f,
                appearancePreset: previewPreset);
        }
        else
        {
            _geometryBatch.FillCircle(new Vector2(previewCx, previewCy + 6f), 11f, new ColorRgba(56, 189, 248, 255), 14);
            _geometryBatch.FillCircle(new Vector2(previewCx, previewCy - 9f), 9f, new ColorRgba(253, 224, 179, 255), 12);
        }

        // Vector portrait badges inside occupied character cards
        for (int i = 0; i < 4 && i < _characters.Count; i++)
        {
            RectangleF b = _slotBounds[i];
            if (_vectorArt is not null && _vectorArt.TryGetCharacter(_characters[i].AppearancePreset, out CompiledVectorRig? cardRig))
            {
                RectangleF pBox = new(b.X + 8f, b.Y + 8f, 44f, 44f);
                _geometryBatch.FillRectangle(pBox, new ColorRgba(12, 18, 32, 235));
                _geometryBatch.DrawRectangle(pBox, cardRig.AccentColor, 1.2f);
                VectorRigRenderer.DrawPortraitRig(
                    _geometryBatch,
                    cardRig,
                    new Vector2(b.X + 30f, b.Y + 30f),
                    boxSize: 38f,
                    Direction8.South,
                    "idle",
                    _elapsed + (i * 0.25f),
                    appearancePreset: _characters[i].AppearancePreset);
            }
        }

        _createNameField.DrawGeometry(_geometryBatch, _font);
        _enterWorldButton.DrawGeometry(_geometryBatch);
        _createButton.DrawGeometry(_geometryBatch);
        _hairPrevBtn.DrawGeometry(_geometryBatch);
        _hairNextBtn.DrawGeometry(_geometryBatch);
        _hColPrevBtn.DrawGeometry(_geometryBatch);
        _hColNextBtn.DrawGeometry(_geometryBatch);
        _facePrevBtn.DrawGeometry(_geometryBatch);
        _faceNextBtn.DrawGeometry(_geometryBatch);
        _skinPrevBtn.DrawGeometry(_geometryBatch);
        _skinNextBtn.DrawGeometry(_geometryBatch);
        _logoutButton.DrawGeometry(_geometryBatch);
        _geometryBatch.End();

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: proj);
        ColorRgba black = new(8, 10, 16, 255);

        spriteBatch.DrawOutlinedString(_font, "CHARACTER ROSTER", new Vector2(ox + 22f, oy + 16f), new ColorRgba(250, 204, 21, 255), black, 2, 2.5f);
        spriteBatch.DrawOutlinedString(_font, $"TRANSPORT: {_session.TransportLabel}", new Vector2(ox + 500f, oy + 18f), new ColorRgba(125, 211, 252, 255), black, 1, 2f);

        for (int i = 0; i < 4; i++)
        {
            RectangleF b = _slotBounds[i];
            if (i < _characters.Count)
            {
                ClientCharacterSummary c = _characters[i];
                ColorRgba nameColor = i == _selectedIndex ? new ColorRgba(250, 204, 21, 255) : ColorRgba.White;
                float textX = _vectorArt is not null ? b.X + 60f : b.X + 14f;
                spriteBatch.DrawOutlinedString(_font, $"{c.Name}  (Lv.{c.Level})", new Vector2(textX, b.Y + 12f), nameColor, black, 1, 2f);
                spriteBatch.DrawOutlinedString(_font, $"Tier {c.ZoneTier} | Pos ({(int)c.PositionX},{(int)c.PositionY})", new Vector2(textX, b.Y + 38f), new ColorRgba(148, 163, 184, 255), black, 1, 1.5f);
            }
            else
            {
                spriteBatch.DrawOutlinedString(_font, $"SLOT {i + 1} - EMPTY", new Vector2(b.X + 14f, b.Y + 26f), new ColorRgba(100, 116, 139, 255), black, 1, 2f);
            }
        }

        string selText = _selectedIndex >= 0 && _selectedIndex < _characters.Count
            ? $"READY: {_characters[_selectedIndex].Name.ToUpperInvariant()}"
            : "CREATE A HERO FIRST";
        spriteBatch.DrawOutlinedString(_font, selText, new Vector2(ox + 458f, oy + 76f), ColorRgba.White, black, 1, 2f);
        string previewHeader = !_isCustomizingNewHero && _selectedIndex >= 0 && _selectedIndex < _characters.Count
            ? $"HERO: {_characters[_selectedIndex].Name.ToUpperInvariant()}"
            : "NEW HERO BUILDER";
        spriteBatch.DrawOutlinedString(_font, previewHeader, new Vector2(ox + 586f, oy + 154f), new ColorRgba(203, 213, 225, 255), black, 1, 1.5f);
        spriteBatch.DrawOutlinedString(_font, $"HAIR: {s_hairStyles[_hairStyle]}", new Vector2(ox + 618f, oy + 180f), ColorRgba.White, black, 1, 1f);
        spriteBatch.DrawOutlinedString(_font, $"TONE: {s_hairColors[_hairColor]}", new Vector2(ox + 618f, oy + 212f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(_font, $"EYES: {s_faceStyles[_faceStyle]}", new Vector2(ox + 618f, oy + 244f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(_font, $"SKIN: {s_skinTones[_skinTone]}", new Vector2(ox + 618f, oy + 276f), new ColorRgba(245, 198, 165, 255), black, 1, 1f);

        _createNameField.DrawText(spriteBatch, _font);
        _enterWorldButton.DrawText(spriteBatch, _font);
        _createButton.DrawText(spriteBatch, _font);
        _hairPrevBtn.DrawText(spriteBatch, _font);
        _hairNextBtn.DrawText(spriteBatch, _font);
        _hColPrevBtn.DrawText(spriteBatch, _font);
        _hColNextBtn.DrawText(spriteBatch, _font);
        _facePrevBtn.DrawText(spriteBatch, _font);
        _faceNextBtn.DrawText(spriteBatch, _font);
        _skinPrevBtn.DrawText(spriteBatch, _font);
        _skinNextBtn.DrawText(spriteBatch, _font);
        _logoutButton.DrawText(spriteBatch, _font);

        ColorRgba statusColor = _statusIsError
            ? new ColorRgba(248, 113, 113, 255)
            : new ColorRgba(110, 231, 183, 255);
        string status = _statusMessage.Length > 68 ? _statusMessage[..68] : _statusMessage;
        spriteBatch.DrawOutlinedString(_font, status, new Vector2(ox + 22f, oy + totalH - 32f), statusColor, black, 1, 2f);

        spriteBatch.End();
        base.Render(context, spriteBatch, dt);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.DetachInput();
            _vectorArt = null;
            _geometryBatch?.Dispose();
            _geometryBatch = null;
            _font?.Dispose();
            _font = null;
            _whitePixel?.Dispose();
            _whitePixel = null;
        }

        base.Dispose(disposing);
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
                // Always return to LoginScene after logout request settles.
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
            Task<IReadOnlyList<ClientCharacterSummary>> done = _listTask;
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

                if (_createTask is null)
                {
                    _statusIsError = false;
                    _statusMessage = _characters.Count > 0
                        ? $"Found {_characters.Count} hero(es). Select one and click ENTER WORLD."
                        : "No characters yet. Enter a name on the right to create your hero!";
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _statusIsError = true;
                _statusMessage = $"Failed to load roster: {ex.Message}";
            }
        }
    }

    private void LayoutControls(int width, int height)
    {
        const float totalW = 860f;
        const float totalH = 480f;
        float ox = MathF.Floor((width - totalW) * 0.5f);
        float oy = MathF.Floor((height - totalH) * 0.5f) + 12f;

        _logoutButton.Bounds = new RectangleF(ox + totalW - 122f, oy + 9f, 102f, 34f);

        for (int i = 0; i < 4; i++)
        {
            _slotBounds[i] = new RectangleF(ox + 22f, oy + 76f + (i * 88f), 404f, 76f);
        }

        _enterWorldButton.Bounds = new RectangleF(ox + 458f, oy + 100f, 376f, 44f);

        _hairPrevBtn.Bounds = new RectangleF(ox + 584f, oy + 174f, 28f, 26f);
        _hairNextBtn.Bounds = new RectangleF(ox + 806f, oy + 174f, 28f, 26f);
        _hColPrevBtn.Bounds = new RectangleF(ox + 584f, oy + 206f, 28f, 26f);
        _hColNextBtn.Bounds = new RectangleF(ox + 806f, oy + 206f, 28f, 26f);
        _facePrevBtn.Bounds = new RectangleF(ox + 584f, oy + 238f, 28f, 26f);
        _faceNextBtn.Bounds = new RectangleF(ox + 806f, oy + 238f, 28f, 26f);
        _skinPrevBtn.Bounds = new RectangleF(ox + 584f, oy + 270f, 28f, 26f);
        _skinNextBtn.Bounds = new RectangleF(ox + 806f, oy + 270f, 28f, 26f);

        _createNameField.Bounds = new RectangleF(ox + 458f, oy + 318f, 376f, 36f);
        _createButton.Bounds = new RectangleF(ox + 458f, oy + 368f, 376f, 40f);
    }

    private void BeginLogout()
    {
        _logoutTask = _session.LogoutAsync();
        _statusIsError = false;
        _statusMessage = "Signing out...";
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
        if (_createTask is null && _enterTask is null && _logoutTask is null)
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

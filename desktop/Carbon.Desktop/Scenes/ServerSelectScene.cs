// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Input;
using Silk.NET.Input;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// Server screen (ADR-037): one card per server from <c>servers.ini</c> with its friendly name, a status word, a
/// connection word and signal bars (docs/art/menu-ui.md rule 17) - never an address or milliseconds. Each server is
/// checked once per visit. Up/Down (or Tab) choose, Enter picks, Esc goes back. With <c>CARBON_DEV_SERVERS=1</c> a
/// developer can also type a server address.
/// </summary>
internal sealed class ServerSelectScene : MenuScene
{
    private const int MaxListed = 6;
    private const float CardPitch = 74f;

    private readonly List<GameServer> _servers = [];
    private readonly List<MenuButton> _cards = [];
    private readonly ServerStatus[] _status = new ServerStatus[MaxListed];
    private readonly Task<TimeSpan?>?[] _probes = new Task<TimeSpan?>?[MaxListed];
    private readonly CachedText[] _statusLines = [new(), new(), new(), new(), new(), new()];
    private readonly MenuButton _backButton = new(GameText.Get("login.back"), MenuButtonStyle.NeutralSlate);
    private readonly MenuTextField? _customField;
    private readonly MenuButton? _useCustomButton;
    private CancellationTokenSource? _probing;
    private RectangleF _card;
    private int _focus;

    public ServerSelectScene(MenuFlow flow)
        : base(nameof(ServerSelectScene), flow, "server")
    {
        IReadOnlyList<GameServer> servers = flow.Servers.Servers;
        for (int i = 0; i < servers.Count && i < MaxListed; i++)
        {
            _servers.Add(servers[i]);
            _cards.Add(new MenuButton(string.Empty));
            _status[i] = ServerStatus.Checking;
            if (servers[i] == flow.SelectedServer)
            {
                _focus = i;
            }
        }

        if (flow.Options.CustomServerEntry)
        {
            _customField = new MenuTextField(GameText.Get("login.field.server"), GameText.Get("login.hint.server"), string.Empty, isPassword: false, maxLength: 96)
            {
                Icon = MenuFieldIcon.Globe,
            };
            _useCustomButton = new MenuButton(GameText.Get("login.use"), MenuButtonStyle.NeutralSlate);
        }
    }

    /// <summary>Gets the focused row: a server index, or the server count for Back (tests read this).</summary>
    internal int FocusIndex => _focus;

    /// <summary>Gets what the card of server <paramref name="index"/> shows (tests read this).</summary>
    internal ServerStatus StatusOf(int index) => _status[index];

    /// <inheritdoc/>
    protected override string? Header => GameText.Get("login.header.server");

    /// <inheritdoc/>
    protected override string? FooterHint => GameText.Get("login.footer.list");

    /// <summary>Returns the name the player sees for <paramref name="server"/>.</summary>
    public static string DisplayName(GameServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return server.IsCustom ? GameText.Get("login.server.custom") : server.Name;
    }

    /// <inheritdoc/>
    public override void Enter(SceneContext context)
    {
        base.Enter(context);
        this.StartProbes();
    }

    /// <inheritdoc/>
    public override void Leave(SceneContext context)
    {
        this.StopProbes();
        base.Leave(context);
    }

    /// <inheritdoc/>
    protected override RectangleF Layout(int width, int height)
    {
        const float panelW = 540f;
        float listH = _cards.Count * CardPitch;
        float panelH = 76f + listH + (_customField is null ? 0f : 76f) + 52f + (this.StatusMessage.Length > 0 ? 56f : 16f);
        float px = MathF.Floor((width - panelW) * 0.5f);
        float py = PanelTop(height, panelH);
        _card = new RectangleF(px, py, panelW, panelH);

        float fx = px + 32f;
        float fw = panelW - 64f;
        float y = py + 76f;
        for (int i = 0; i < _cards.Count; i++)
        {
            _cards[i].Bounds = new RectangleF(fx, y, fw, CardPitch - 12f);
            _cards[i].Style = _servers[i] == this.Flow.SelectedServer ? MenuButtonStyle.PrimaryGold : MenuButtonStyle.NeutralSlate;
            y += CardPitch;
        }

        if (_customField is not null && _useCustomButton is not null)
        {
            _customField.Bounds = new RectangleF(fx, y + 24f, fw - 92f, 40f);
            _useCustomButton.Bounds = new RectangleF(fx + fw - 84f, y + 24f, 84f, 40f);
            y += 76f;
        }

        _backButton.Bounds = new RectangleF(fx, y + 4f, 120f, 36f);
        return _card;
    }

    /// <inheritdoc/>
    protected override void UpdateMenu(SceneContext context, float dt)
    {
        this.CollectProbes();
        KeyboardSnapshot kb = context.Input.Keyboard;
        MouseSnapshot mouse = context.Input.Mouse;
        bool leftDown = mouse.IsButtonDown(MouseButton.Left);
        bool leftPressed = mouse.IsButtonPressed(MouseButton.Left);
        Vector2 mousePos = this.PointerPosition(in mouse);
        bool typing = _customField is { IsFocused: true };

        if (kb.IsKeyPressed(Key.Escape) || _backButton.Update(mousePos, leftDown, leftPressed, dt))
        {
            ChangeTo(context, () => new StartScene(this.Flow));
            return;
        }

        bool shift = kb.IsKeyDown(Key.ShiftLeft) || kb.IsKeyDown(Key.ShiftRight);
        if (kb.IsKeyPressed(Key.Down) || (kb.IsKeyPressed(Key.Tab) && !shift))
        {
            this.MoveFocus(+1);
        }
        else if (kb.IsKeyPressed(Key.Up) || (kb.IsKeyPressed(Key.Tab) && shift))
        {
            this.MoveFocus(-1);
        }

        for (int i = 0; i < _cards.Count; i++)
        {
            _cards[i].IsFocused = !typing && _focus == i;
            if (_cards[i].Update(mousePos, leftDown, leftPressed, dt))
            {
                this.Choose(context, i);
                return;
            }
        }

        _backButton.IsFocused = !typing && _focus == _cards.Count;
        bool enter = kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter);
        if (_customField is not null && _useCustomButton is not null)
        {
            _customField.Update(dt, mousePos, leftPressed);
            if (_useCustomButton.Update(mousePos, leftDown, leftPressed, dt) || (enter && _customField.IsFocused))
            {
                if (TryParseAddress(_customField.Text, out string host, out ushort port))
                {
                    this.Flow.SelectedServer = this.Flow.Servers.Find(host, port) ?? new GameServer(ServerDirectory.CustomId, string.Empty, host, port);
                    ChangeTo(context, () => new StartScene(this.Flow));
                    return;
                }

                this.SetStatus(GameText.Get("login.error.invalid_gateway"), isError: true);
                return;
            }
        }

        if (!typing && (enter || kb.IsKeyPressed(Key.Space)))
        {
            if (_focus < _cards.Count)
            {
                this.Choose(context, _focus);
            }
            else
            {
                ChangeTo(context, () => new StartScene(this.Flow));
            }
        }
    }

    /// <inheritdoc/>
    protected override void DrawSkins(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        foreach (MenuButton card in _cards)
        {
            card.DrawSkin(spriteBatch, skin);
        }

        _customField?.DrawSkin(spriteBatch, skin);
        _useCustomButton?.DrawSkin(spriteBatch, skin);
        _backButton.DrawSkin(spriteBatch, skin);
    }

    /// <inheritdoc/>
    protected override void DrawGeometry(GeometryBatch geometry, BitmapFont font)
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            MenuButton card = _cards[i];
            card.DrawOverlay(geometry);
            RectangleF b = card.Bounds;
            Vector2 icon = new(b.X + 26f, b.Y + (b.Height * 0.5f));
            if (_servers[i] == this.Flow.SelectedServer)
            {
                MenuIcons.DrawCheck(geometry, icon, CarbonUiPalette.BorderActive);
            }
            else
            {
                MenuIcons.DrawField(geometry, MenuFieldIcon.Globe, icon, MenuPalette.Icon);
            }

            DrawSignal(geometry, b, _status[i]);
        }

        _customField?.DrawCursorGeometry(geometry, font);
        _useCustomButton?.DrawOverlay(geometry);
        _backButton.DrawOverlay(geometry);
    }

    /// <inheritdoc/>
    protected override void DrawTexts(SpriteBatch spriteBatch, BitmapFont font)
    {
        ColorRgba black = CarbonUiPalette.TextOutline;
        for (int i = 0; i < _cards.Count; i++)
        {
            RectangleF b = _cards[i].Bounds;
            bool selected = _servers[i] == this.Flow.SelectedServer;
            float textW = b.Width - 52f - 64f;
            string name = CarbonUiSkin.TruncateToWidth(font, DisplayName(_servers[i]), textW, 1f);
            spriteBatch.DrawOutlinedString(font, name, new Vector2(b.X + 52f, b.Y + 11f), selected ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextPrimary, black, 1, 1f);

            ServerStatus status = _status[i];
            string line = status.QualityKey is { } qualityKey
                ? _statusLines[i].Format("login.server_status.line", new TextArg("status", GameText.Get(status.StatusKey)), new TextArg("quality", GameText.Get(qualityKey)))
                : GameText.Get(status.StatusKey);
            ColorRgba color = status.Health switch
            {
                ServerHealth.Online => MenuPalette.SuccessText,
                ServerHealth.Unreachable => MenuPalette.ErrorText,
                ServerHealth.Checking => CarbonUiPalette.TextMuted,
                _ => CarbonUiPalette.TextMuted,
            };
            spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, line, textW, 1f), new Vector2(b.X + 52f, b.Y + 33f), color, black, 1, 1f);
        }

        _customField?.DrawText(spriteBatch, font);
        _useCustomButton?.DrawText(spriteBatch, font);
        _backButton.DrawText(spriteBatch, font);
    }

    /// <inheritdoc/>
    protected override void OnCharacter(char ch) => _customField?.AppendChar(ch);

    /// <inheritdoc/>
    protected override void OnBackspace() => _customField?.Backspace();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.StopProbes();
        }

        base.Dispose(disposing);
    }

    /// <summary>Parses <c>host</c> or <c>host:port</c> (default port 57207).</summary>
    internal static bool TryParseAddress(string raw, out string host, out ushort port)
    {
        host = string.Empty;
        port = 57207;
        string trimmed = raw?.Trim() ?? string.Empty;
        int colon = trimmed.LastIndexOf(':');
        if (colon > 0)
        {
            if (!ushort.TryParse(trimmed[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port == 0)
            {
                return false;
            }

            trimmed = trimmed[..colon].Trim();
        }

        host = trimmed;
        return host.Length > 0 && !host.Contains(' ', StringComparison.Ordinal);
    }

    /// <summary>Four signal bars at the right of a card, lit by the connection quality.</summary>
    private static void DrawSignal(GeometryBatch geometry, in RectangleF card, ServerStatus status)
    {
        ColorRgba lit = status.Quality switch
        {
            ConnectionQuality.Excellent or ConnectionQuality.Good => MenuPalette.SuccessText,
            ConnectionQuality.Fair => MenuPalette.WarningText,
            ConnectionQuality.Poor => MenuPalette.ErrorText,
            ConnectionQuality.Unknown => MenuPalette.MeterTrack,
            _ => MenuPalette.MeterTrack,
        };
        float x = card.Right - 50f;
        float baseY = card.Y + (card.Height * 0.5f) + 10f;
        for (int bar = 0; bar < 4; bar++)
        {
            float h = 6f + (bar * 5f);
            geometry.FillRectangle(new RectangleF(x + (bar * 8f), baseY - h, 5f, h), bar < status.Bars ? lit : MenuPalette.MeterTrack);
        }

        if (status.Health == ServerHealth.Unreachable)
        {
            geometry.DrawLine(new Vector2(x - 2f, baseY - 20f), new Vector2(x + 31f, baseY + 1f), MenuPalette.ErrorText, 2f);
        }
    }

    private void Choose(SceneContext context, int index)
    {
        this.Flow.SelectedServer = _servers[index];
        ChangeTo(context, () => new StartScene(this.Flow));
    }

    /// <summary>Moves keyboard focus over the cards then Back, wrapping.</summary>
    private void MoveFocus(int step)
    {
        int count = _cards.Count + 1;
        _focus = (((_focus + step) % count) + count) % count;
        _customField?.IsFocused = false;
    }

    /// <summary>Checks every listed server once, in the background.</summary>
    private void StartProbes()
    {
        this.StopProbes();
        _probing = new CancellationTokenSource();
        for (int i = 0; i < _servers.Count; i++)
        {
            _status[i] = ServerStatus.Checking;
            _probes[i] = this.Flow.Probe.ProbeAsync(_servers[i], _probing.Token);
        }
    }

    private void CollectProbes()
    {
        for (int i = 0; i < _servers.Count; i++)
        {
            if (_probes[i] is not { IsCompleted: true } probe)
            {
                continue;
            }

            _probes[i] = null;
            _status[i] = ServerStatus.FromProbe(probe.IsCompletedSuccessfully ? probe.Result : null);
        }
    }

    private void StopProbes()
    {
        _probing?.Cancel();
        _probing?.Dispose();
        _probing = null;
    }
}

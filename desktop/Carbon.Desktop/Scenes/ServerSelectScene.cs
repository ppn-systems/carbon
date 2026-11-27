// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
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
/// Server screen (ADR-037): pick a server by its friendly name from <c>servers.ini</c>. With
/// <c>CARBON_DEV_SERVERS=1</c> a developer can also type a server address.
/// </summary>
internal sealed class ServerSelectScene : MenuScene
{
    private const int MaxListed = 6;

    private readonly List<(GameServer Server, MenuButton Button)> _rows = [];
    private readonly MenuButton _backButton = new(GameText.Get("login.back"), MenuButtonStyle.NeutralSlate);
    private readonly MenuTextField? _customField;
    private readonly MenuButton? _useCustomButton;
    private RectangleF _card;

    public ServerSelectScene(MenuFlow flow)
        : base(nameof(ServerSelectScene), flow, "server")
    {
        IReadOnlyList<GameServer> servers = flow.Servers.Servers;
        for (int i = 0; i < servers.Count && i < MaxListed; i++)
        {
            _rows.Add((servers[i], new MenuButton(DisplayName(servers[i]))));
        }

        if (flow.Options.CustomServerEntry)
        {
            _customField = new MenuTextField(GameText.Get("login.field.server"), GameText.Get("login.hint.server"), string.Empty, isPassword: false, maxLength: 96)
            {
                IsFocused = true,
            };
            _useCustomButton = new MenuButton(GameText.Get("login.use"), MenuButtonStyle.NeutralSlate);
        }
    }

    /// <inheritdoc/>
    protected override string? Header => GameText.Get("login.header.server");

    /// <summary>Returns the name the player sees for <paramref name="server"/>.</summary>
    public static string DisplayName(GameServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return server.IsCustom ? GameText.Get("login.server.custom") : server.Name;
    }

    /// <inheritdoc/>
    protected override RectangleF Layout(int width, int height)
    {
        const float panelW = 500f;
        float listH = _rows.Count * 52f;
        float panelH = 208f + listH + (_customField is null ? 0f : 70f);
        float px = MathF.Floor((width - panelW) * 0.5f);
        float py = MathF.Floor((height - panelH) * 0.5f) + 22f;
        _card = new RectangleF(px, py, panelW, panelH);

        float fx = px + 28f;
        float fw = panelW - 56f;
        float y = py + 76f;
        foreach ((GameServer server, MenuButton button) in _rows)
        {
            button.Bounds = new RectangleF(fx, y, fw, 42f);
            button.Style = server == this.Flow.SelectedServer ? MenuButtonStyle.PrimaryGold : MenuButtonStyle.NeutralSlate;
            y += 52f;
        }

        if (_customField is not null && _useCustomButton is not null)
        {
            _customField.Bounds = new RectangleF(fx, y + 24f, fw - 92f, 38f);
            _useCustomButton.Bounds = new RectangleF(fx + fw - 84f, y + 24f, 84f, 38f);
            y += 70f;
        }

        _backButton.Bounds = new RectangleF(fx, y + 8f, 120f, 36f);
        return _card;
    }

    /// <inheritdoc/>
    protected override void UpdateMenu(SceneContext context, float dt)
    {
        KeyboardSnapshot kb = context.Input.Keyboard;
        MouseSnapshot mouse = context.Input.Mouse;
        bool leftDown = mouse.IsButtonDown(MouseButton.Left);
        bool leftPressed = mouse.IsButtonPressed(MouseButton.Left);
        Vector2 mousePos = mouse.Position;

        if (kb.IsKeyPressed(Key.Escape) || _backButton.Update(mousePos, leftDown, leftPressed))
        {
            ChangeTo(context, () => new StartScene(this.Flow));
            return;
        }

        foreach ((GameServer server, MenuButton button) in _rows)
        {
            if (button.Update(mousePos, leftDown, leftPressed))
            {
                this.Flow.SelectedServer = server;
                ChangeTo(context, () => new StartScene(this.Flow));
                return;
            }
        }

        if (_customField is not null && _useCustomButton is not null)
        {
            _customField.Update(dt, mousePos, leftPressed);
            bool enter = kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter);
            if (_useCustomButton.Update(mousePos, leftDown, leftPressed) || enter)
            {
                if (TryParseAddress(_customField.Text, out string host, out ushort port))
                {
                    this.Flow.SelectedServer = this.Flow.Servers.Find(host, port) ?? new GameServer(ServerDirectory.CustomId, string.Empty, host, port);
                    ChangeTo(context, () => new StartScene(this.Flow));
                    return;
                }

                this.SetStatus(GameText.Get("login.error.invalid_gateway"), isError: true);
            }
        }
    }

    /// <inheritdoc/>
    protected override void DrawSkins(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        foreach ((_, MenuButton button) in _rows)
        {
            button.DrawSkin(spriteBatch, skin);
        }

        _customField?.DrawSkin(spriteBatch, skin);
        _useCustomButton?.DrawSkin(spriteBatch, skin);
        _backButton.DrawSkin(spriteBatch, skin);
    }

    /// <inheritdoc/>
    protected override void DrawGeometry(GeometryBatch geometry, BitmapFont font) =>
        _customField?.DrawCursorGeometry(geometry, font);

    /// <inheritdoc/>
    protected override void DrawTexts(SpriteBatch spriteBatch, BitmapFont font)
    {
        foreach ((_, MenuButton button) in _rows)
        {
            button.DrawText(spriteBatch, font);
        }

        _customField?.DrawText(spriteBatch, font);
        _useCustomButton?.DrawText(spriteBatch, font);
        _backButton.DrawText(spriteBatch, font);
    }

    /// <inheritdoc/>
    protected override void OnCharacter(char ch) => _customField?.AppendChar(ch);

    /// <inheritdoc/>
    protected override void OnBackspace() => _customField?.Backspace();

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
}

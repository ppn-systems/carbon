// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Input;
using Carbon.Desktop.Rendering.Hud;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// The scene's side of the repair window (ADR-028): <c>N</c> opens it at a player station, the blacksmith's trade window
/// opens it at the blacksmith, and clicks select one item or send its repair. The server validates and spends everything.
/// </summary>
/// <param name="session">World session.</param>
/// <param name="hud">HUD that draws the window.</param>
/// <param name="options">Desktop options (the screenshot tool's <c>CARBON_OPEN_REPAIR</c> and <c>CARBON_REPAIR_PREVIEW</c>).</param>
internal sealed class RepairInteraction(IWorldSession session, HudOverlayRenderer hud, DesktopOptions options)
{
    private readonly IWorldSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly HudOverlayRenderer _hud = hud ?? throw new ArgumentNullException(nameof(hud));
    private readonly DesktopOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private bool _captureOpened;

    /// <summary>Gets whether a screenshot should wait for the repair window (up to <paramref name="frame"/> 150).</summary>
    public bool IsCaptureWaiting(int frame) => _options.OpenRepair != RepairVenue.None && !_captureOpened && frame < 150;

    /// <summary>Opens the window at a player station, or closes it when it is open (the <c>N</c> key).</summary>
    public void Toggle()
    {
        if (_hud.Repair.IsOpen)
        {
            _hud.Repair.Close();
            return;
        }

        this.Open(RepairVenue.Station);
    }

    /// <summary>Opens the window at <paramref name="venue"/>, closing any other window.</summary>
    public void Open(RepairVenue venue)
    {
        _hud.CloseAllModals();
        _hud.Repair.Open(venue);
    }

    /// <summary>Advances the animation; a screenshot run opens the window (and its preview) once the world is in.</summary>
    public void Update(float dt, int frame)
    {
        if (_options.OpenRepair != RepairVenue.None && !_captureOpened && frame >= 30)
        {
            _captureOpened = true;
            this.Open(_options.OpenRepair);
            this.StartPreview();
        }

        _hud.Repair.Update(dt, _session.Repair);
    }

    /// <summary>Handles a click in the open window.</summary>
    public void HandleWindowInput(DesktopInputFrame input, int width, int height)
    {
        if (!input.LeftClickPressed || !_hud.Repair.IsOpen)
        {
            return;
        }

        if (!RepairModalWidget.ContainsPoint(input.MouseScreenPosition, width, height))
        {
            _hud.Repair.Close();
            return;
        }

        switch (_hud.Repair.HandleClick(input.MouseScreenPosition, _session, width, height))
        {
            case RepairClick.Close:
                _hud.Repair.Close();
                break;
            case RepairClick.RepairFull:
                _ = _hud.Repair.TryStart(_session, _session.Repair, partial: false);
                break;
            case RepairClick.RepairPartial:
                _ = _hud.Repair.TryStart(_session, _session.Repair, partial: true);
                break;
            case RepairClick.None:
            case RepairClick.Consumed:
            default:
                break;
        }
    }

    // CARBON_REPAIR_PREVIEW=work|success[:seconds]: plays (or freezes) the animation on the selected item. Visual only.
    private void StartPreview()
    {
        if (_options.RepairPreview is not string spec)
        {
            return;
        }

        string[] parts = spec.Split(':', 2);
        bool success = string.Equals(parts[0], "success", StringComparison.OrdinalIgnoreCase);
        float seconds = 0f;
        bool frozen = parts.Length == 2 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
        _ = _hud.Repair.Preview(_session, success, seconds, frozen);
        Console.WriteLine($"[RepairPreview] {(success ? "success" : "work")} {(frozen ? $"frozen at {seconds.ToString(CultureInfo.InvariantCulture)} s" : "playing")}");
    }
}

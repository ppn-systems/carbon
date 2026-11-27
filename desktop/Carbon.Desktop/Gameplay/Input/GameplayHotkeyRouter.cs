// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Gameplay.Repair;
using Carbon.Desktop.Input;
using Carbon.Desktop.Rendering.Hud;

namespace Carbon.Desktop.Gameplay.Input;

/// <summary>
/// The world scene's window hotkeys (moved out of <c>WorldGameplayScene.Update</c>): the Escape cascade (dialog, status
/// details, settings, windows, locked target, then the settings menu) and the keys that toggle the HUD, minimap, rest,
/// target cycling, the player-window tabs, the forge and the repair window.
/// </summary>
internal sealed class GameplayHotkeyRouter
{
    private readonly IWorldSession _session;
    private readonly HudOverlayRenderer _hud;
    private readonly HudUiState _state;
    private readonly InventoryInputController _inventoryInput;
    private readonly RepairInteraction _repair;
    private readonly Action _toggleForge;

    /// <summary>Initializes a new instance of the <see cref="GameplayHotkeyRouter"/> class.</summary>
    /// <param name="session">World session.</param>
    /// <param name="hud">HUD renderer (inventory dialog, status details, toasts, settings).</param>
    /// <param name="state">HUD window state.</param>
    /// <param name="inventoryInput">Inventory input, whose drag a window change resets.</param>
    /// <param name="repair">Repair window interaction.</param>
    /// <param name="toggleForge">Opens or closes the forge window.</param>
    public GameplayHotkeyRouter(IWorldSession session, HudOverlayRenderer hud, HudUiState state, InventoryInputController inventoryInput, RepairInteraction repair, Action toggleForge)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _inventoryInput = inventoryInput ?? throw new ArgumentNullException(nameof(inventoryInput));
        _repair = repair ?? throw new ArgumentNullException(nameof(repair));
        _toggleForge = toggleForge ?? throw new ArgumentNullException(nameof(toggleForge));
    }

    /// <summary>Handles Escape: closes the innermost open thing, or opens the settings menu when nothing is open.</summary>
    /// <param name="input">This frame's input.</param>
    public void RouteEscape(in DesktopInputFrame input)
    {
        if (input.EscapePressed)
        {
            if (_hud.InventoryDialog.IsOpen)
            {
                _hud.InventoryDialog.Close();
            }
            else if (_hud.CloseStatusEffectDetails())
            {
                // ADR-029: Esc closes the status effect details first.
            }
            else if (_state.ShowSettingsModal)
            {
                _state.ShowSettingsModal = false;
            }
            else if (_state.HasActiveModal)
            {
                _state.CloseAllModals();
                _inventoryInput.ResetDragState();
            }
            else if (_session.LockedTargetNetworkId != 0UL)
            {
                _session.SetLockedTarget(0UL);
            }
            else
            {
                _state.ShowSettingsModal = true;
            }
        }
    }

    /// <summary>Handles the window and HUD toggle keys (runs only while the settings menu is closed).</summary>
    /// <param name="input">This frame's input.</param>
    public void RouteToggles(in DesktopInputFrame input)
    {
        if (input.ToggleHudPressed)
        {
            _state.Visible = !_state.Visible;
        }

        if (input.ToggleMinimapPressed && !_state.HasActiveModal)
        {
            _hud.Settings.ShowMinimap = !_hud.Settings.ShowMinimap;
            _hud.Settings.Save();
        }

        if (input.ToggleRestPressed && !_state.HasActiveModal)
        {
            _ = _session.TryToggleRest();
        }

        if (input.CycleTargetPressed && !_state.HasActiveModal)
        {
            _ = _session.CycleNearestHostileTarget();
        }

        if (input.ToggleInventoryPressed)
        {
            _inventoryInput.ResetDragState();
            _ = _state.OpenOrTogglePlayerTab(_session, PlayerModalTab.Inventory);
        }

        if (input.ToggleCharacterPressed)
        {
            _inventoryInput.ResetDragState();
            _ = _state.OpenOrTogglePlayerTab(_session, PlayerModalTab.Character);
        }

        if (input.ToggleSkillsPressed)
        {
            _inventoryInput.ResetDragState();
            if (!_state.OpenOrTogglePlayerTab(_session, PlayerModalTab.Skills))
            {
                _hud.PushNotification(GameText.Get("notice.skills_need_class"));
            }
        }

        if (input.ToggleCraftPressed)
        {
            _inventoryInput.ResetDragState();
            _ = _state.OpenOrTogglePlayerTab(_session, PlayerModalTab.Crafting);
        }

        if (input.ToggleForgePressed)
        {
            _toggleForge();
        }

        if (input.ToggleRepairPressed)
        {
            _inventoryInput.ResetDragState();
            _repair.Toggle();
        }
    }
}

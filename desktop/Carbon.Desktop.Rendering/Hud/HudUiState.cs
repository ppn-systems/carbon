// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Desktop.Abstractions.World;
using Carbon.Items.Catalog;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Which HUD windows are open and what they show (audit OOP-3): the player window and its tab, the forge, trader, repair and
/// settings windows, the trade basket, the carried inventory slot, the interact prompt and the screenshot death preview.
/// The scene owns it and drives it; <see cref="HudOverlayRenderer"/> only reads it. Needs no GL context.
/// </summary>
public sealed class HudUiState
{
    private readonly List<SkillDefinition> _skillScratch = new(8);

    /// <summary>Gets or sets whether the HUD overlay is visible.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Gets or sets whether the unified tabbed player window (Inventory/Character/Crafting/Skills) is open.</summary>
    public bool ShowPlayerModal { get; set; }

    /// <summary>Gets or sets the active tab of the unified player modal window.</summary>
    public PlayerModalTab ActivePlayerTab { get; set; } = PlayerModalTab.Inventory;

    /// <summary>Gets or sets whether the Blacksmith Forge Anvil modal is open.</summary>
    public bool ShowForgeModal { get; set; }

    /// <summary>Gets or sets whether the System Settings &amp; Controls modal is open.</summary>
    public bool ShowSettingsModal { get; set; }

    /// <summary>Gets or sets the open trader window's stock (ADR-025), or <see langword="null"/> when it is closed.</summary>
    public TraderView? Trader { get; set; }

    /// <summary>Gets the trade window's staged sell basket and buy cart (ADR-025 D18).</summary>
    public TradeBasket TradeBasket { get; } = new();

    /// <summary>Gets or sets the bag slot being dragged towards the basket, or -1.</summary>
    public int TradeDragSlot { get; set; } = -1;

    /// <summary>Gets the repair window (ADR-028); open while <see cref="RepairWindowState.IsOpen"/>.</summary>
    public RepairWindowState Repair { get; } = new();

    /// <summary>Gets or sets the inventory slot index currently held/carried by the cursor, or <c>-1</c>.</summary>
    public int CarriedInventorySlot { get; set; } = -1;

    /// <summary>
    /// Gets or sets a death shown instead of the session's (screenshot preview only, <c>CARBON_UI_PREVIEW=death</c>).
    /// Visual only: nothing is sent to the server.
    /// </summary>
    public DeathView? DeathPreview { get; set; }

    /// <summary>Gets or sets the interact-key prompt to draw this frame (hidden while a window is open).</summary>
    public InteractionPrompt? InteractPrompt { get; set; }

    /// <summary>Gets whether any full-screen modal (Player Window, Forge, Settings, Trader or Repair) is open.</summary>
    public bool HasActiveModal =>
        this.ShowPlayerModal || this.ShowForgeModal || this.ShowSettingsModal || this.Trader is not null || this.Repair.IsOpen;

    /// <summary>Returns whether <paramref name="world"/> currently has at least one accessible class skill.</summary>
    public bool HasAccessibleClassSkills(IWorldReadModel world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return PlayerModalTabs.HasClassSkills(world, _skillScratch);
    }

    /// <summary>
    /// Opens or switches to <paramref name="tab"/>, or closes the player modal if already viewing <paramref name="tab"/>.
    /// Returns <see langword="false"/> if <paramref name="tab"/> is <see cref="PlayerModalTab.Skills"/> and the character has no class skills.
    /// </summary>
    public bool OpenOrTogglePlayerTab(IWorldReadModel world, PlayerModalTab tab)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (tab == PlayerModalTab.Skills && !this.HasAccessibleClassSkills(world))
        {
            return false;
        }

        if (this.ShowPlayerModal && this.ActivePlayerTab == tab)
        {
            this.ShowPlayerModal = false;
            this.CarriedInventorySlot = -1;
            return true;
        }

        this.ShowForgeModal = false;
        this.ShowSettingsModal = false;
        this.Repair.Close();
        this.ShowPlayerModal = true;
        this.ActivePlayerTab = tab;
        if (tab != PlayerModalTab.Inventory)
        {
            this.CarriedInventorySlot = -1;
        }

        return true;
    }

    /// <summary>Closes every window and opens the Settings modal.</summary>
    public void OpenSettings()
    {
        this.CloseAllModals();
        this.ShowSettingsModal = true;
    }

    /// <summary>Closes all active modals cleanly.</summary>
    public void CloseAllModals(bool keepSettings = false)
    {
        this.ShowPlayerModal = false;
        this.ShowForgeModal = false;
        this.Trader = null;
        this.Repair.Close();
        if (!keepSettings)
        {
            this.ShowSettingsModal = false;
        }

        this.CarriedInventorySlot = -1;
    }
}

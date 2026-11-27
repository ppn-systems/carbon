// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Gameplay.Interaction;
using Carbon.Desktop.Input;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;

namespace Carbon.Desktop.Gameplay.Trade;

/// <summary>Says whether a replicated entity is a trader NPC (the scene owns the actor catalog lookup).</summary>
/// <param name="entity">Replicated entity.</param>
/// <returns><see langword="true"/> for a trader.</returns>
internal delegate bool TraderFilter(in RemoteEntityView entity);

/// <summary>
/// The scene's side of trading with village traders (ADR-025): which trader a click talks to (E goes through
/// <see cref="InteractionController"/>), when the window opens and closes, and routing the mouse to
/// <see cref="TradeWindowController"/>, which stages trades in the basket and cart (D18). Closing the window, by any
/// route, returns everything staged. The server validates every request.
/// </summary>
/// <param name="session">World session.</param>
/// <param name="hud">HUD that draws the window.</param>
/// <param name="options">Desktop options (the screenshot tool's <c>CARBON_OPEN_TRADER</c>).</param>
/// <param name="isTrader">Identifies trader NPCs.</param>
/// <param name="openForge">Opens the forge enhancement window (ADR-024): the blacksmith's Enhance button hands off to it.</param>
/// <param name="openRepair">Opens the repair window at the blacksmith (ADR-028): the blacksmith's Repair button hands off to it.</param>
internal sealed class TradeInteraction(IWorldSession session, HudOverlayRenderer hud, DesktopOptions options, TraderFilter isTrader, Func<bool> openForge, Action openRepair)
{
    /// <summary>UI: distance in world pixels beyond which an open trade window closes.</summary>
    private const float WindowRadius = 190f;

    /// <summary>UI: a click on an NPC within this many world pixels of it talks to it.</summary>
    private const float ClickRadius = 24f;

    private readonly IWorldSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly HudOverlayRenderer _hud = hud ?? throw new ArgumentNullException(nameof(hud));
    private readonly DesktopOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TraderFilter _isTrader = isTrader ?? throw new ArgumentNullException(nameof(isTrader));
    private readonly Func<bool> _openForge = openForge ?? throw new ArgumentNullException(nameof(openForge));
    private readonly Action _openRepair = openRepair ?? throw new ArgumentNullException(nameof(openRepair));
    private readonly TradeWindowController _window = new(hud.TradeBasket);
    private bool _shown;
    private bool _previewed;

    /// <summary>Gets whether a screenshot should wait for the trader window (up to <paramref name="frame"/> 150).</summary>
    public bool IsCaptureWaiting(int frame) => _options.OpenTrader && _session.Trading.ActiveTrader is null && frame < 150;

    /// <summary>
    /// Mirrors the server's stock into the HUD once per frame. The window closes when the HUD closed it (Escape, X, a click
    /// outside, another window) or the trader is out of reach; a screenshot run opens the nearest trader.
    /// </summary>
    public void Sync(int frame)
    {
        ITraderSession trading = _session.Trading;
        TraderView? active = trading.ActiveTrader;
        if (active is not null &&
            ((_shown && _hud.Trader is null) || _hud.ShowPlayerModal || _hud.ShowForgeModal || _hud.Repair.IsOpen || !this.IsNear(active.TraderNetworkId)))
        {
            trading.Close();
            active = null;
        }

        _hud.Trader = active;
        _shown = active is not null;
        Span<ItemSlot> bag = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        _session.CopyInventorySlots(bag);
        _window.Sync(active, bag, trading);
        _hud.TradeDragSlot = _window.DraggedSlot;
        if (active is not null)
        {
            this.StagePreview(active, bag);
        }

        if (_options.OpenTrader && active is null && frame >= 6 && frame % 10 == 6 &&
            this.TryFindNearest(_session.LocalPlayerInterpolatedPosition, WindowRadius, out ulong nearest))
        {
            _ = trading.TryOpen(nearest);
        }
    }

    /// <summary>Opens the trader under the cursor.</summary>
    public bool TryClick(Vector2 aimWorld) =>
        this.TryFindNearest(aimWorld, ClickRadius, out ulong trader) && _session.Trading.TryOpen(trader);

    /// <summary>
    /// Handles the mouse in the open window, once a frame: the X or a click outside closes it (returning what is staged);
    /// Repair and Enhance hand off to their windows at a blacksmith (ADR-025 D17); everything else stages trades.
    /// </summary>
    public void HandleWindowInput(DesktopInputFrame input, int width, int height)
    {
        if (_hud.Trader is not TraderView trader)
        {
            return;
        }

        Vector2 mouse = input.MouseScreenPosition;
        if (input.LeftClickPressed &&
            (_hud.TryHitTestModalCloseButton(mouse, width, height) || !_hud.IsPointInActiveModal(_session, mouse, width, height)))
        {
            _hud.Trader = null;
            return;
        }

        TradeWindowHit hit = TradeModalWidget.HitTest(mouse, trader, _hud.TradeBasket, _session, width, height);
        if (input.LeftClickPressed && hit.Target == TradeWindowTarget.Repair)
        {
            _openRepair();
            return;
        }

        if (input.LeftClickPressed && hit.Target == TradeWindowTarget.Enhance)
        {
            _ = _openForge();
            return;
        }

        Span<ItemSlot> bag = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        _session.CopyInventorySlots(bag);
        string? toast = _window.Handle(hit, input.LeftClickPressed, input.LeftMouseDown, input.ShiftHeld, mouse, bag, trader, _session.LocalPlayerGold, _session.Trading);
        _hud.TradeDragSlot = _window.DraggedSlot;
        if (toast is not null)
        {
            _hud.PushNotification(toast);
        }
    }

    // CARBON_TRADE_PREVIEW=basket (screenshots): stages every sellable bag stack and the first two stock rows locally.
    // Visual only: nothing is sent until a button is pressed.
    private void StagePreview(TraderView trader, ReadOnlySpan<ItemSlot> bag)
    {
        if (_previewed || !_options.TradePreview)
        {
            return;
        }

        _previewed = true;
        TradeBasket basket = _hud.TradeBasket;
        for (int s = 0; s < PlayerInventoryLayout.BagLimit; s++)
        {
            _ = basket.AddFromBag(s, bag[s], trader, wholeStack: true);
        }

        _ = basket.AddFromStock(0, trader, 1);
        _ = basket.AddFromStock(1, trader, 3);
    }

    private bool TryFindNearest(Vector2 position, float reach, out ulong networkId)
    {
        networkId = 0UL;
        float best = reach * reach;
        IReadOnlyList<RemoteEntityView> remotes = _session.RemoteEntities;
        for (int i = 0; i < remotes.Count; i++)
        {
            RemoteEntityView entity = remotes[i];
            float d = Vector2.DistanceSquared(position, entity.Position);
            if (d <= best && _isTrader(in entity))
            {
                best = d;
                networkId = entity.NetworkId;
            }
        }

        return networkId != 0UL;
    }

    private bool IsNear(ulong traderNetworkId)
    {
        IReadOnlyList<RemoteEntityView> remotes = _session.RemoteEntities;
        Vector2 me = _session.LocalPlayerInterpolatedPosition;
        for (int i = 0; i < remotes.Count; i++)
        {
            if (remotes[i].NetworkId == traderNetworkId)
            {
                return Vector2.DistanceSquared(me, remotes[i].Position) <= WindowRadius * WindowRadius;
            }
        }

        return false;
    }
}

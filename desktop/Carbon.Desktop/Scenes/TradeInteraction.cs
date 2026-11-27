// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Input;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;

namespace Carbon.Desktop.Scenes;

/// <summary>Says whether a replicated entity is a trader NPC (the scene owns the actor catalog lookup).</summary>
/// <param name="entity">Replicated entity.</param>
/// <returns><see langword="true"/> for a trader.</returns>
internal delegate bool TraderFilter(in RemoteEntityView entity);

/// <summary>
/// The scene's side of trading with village traders (ADR-025): which trader the player can talk to (E or a click), when
/// the window opens and closes, and what a click in it does. The server validates every request.
/// </summary>
/// <param name="session">World session.</param>
/// <param name="hud">HUD that draws the window.</param>
/// <param name="isTrader">Identifies trader NPCs.</param>
/// <param name="captureOpen">Opens the nearest trader for a screenshot (<c>CARBON_OPEN_TRADER</c>).</param>
internal sealed class TradeInteraction(IWorldSession session, HudOverlayRenderer hud, TraderFilter isTrader, bool captureOpen)
{
    /// <summary>UI: distance in world pixels within which the interact key opens a trader (the server re-checks 175 px).</summary>
    private const float TalkRadius = 150f;

    /// <summary>UI: distance in world pixels beyond which an open trade window closes.</summary>
    private const float WindowRadius = 190f;

    /// <summary>UI: a click on an NPC within this many world pixels of it talks to it.</summary>
    private const float ClickRadius = 24f;

    /// <summary>UI: Shift+click buys this many units.</summary>
    private const ushort BulkBuy = 5;

    private readonly IWorldSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly HudOverlayRenderer _hud = hud ?? throw new ArgumentNullException(nameof(hud));
    private readonly TraderFilter _isTrader = isTrader ?? throw new ArgumentNullException(nameof(isTrader));
    private bool _shown;

    /// <summary>Gets whether a screenshot should wait for the trader window (up to <paramref name="frame"/> 150).</summary>
    public bool IsCaptureWaiting(int frame) => captureOpen && _session.Trading.ActiveTrader is null && frame < 150;

    /// <summary>
    /// Mirrors the server's stock into the HUD once per frame. The window closes when the HUD closed it (Escape, X, a click
    /// outside, another window) or the trader is out of reach; a screenshot run opens the nearest trader.
    /// </summary>
    public void Sync(int frame)
    {
        ITraderSession trading = _session.Trading;
        TraderView? active = trading.ActiveTrader;
        if (active is not null &&
            ((_shown && _hud.Trader is null) || _hud.ShowPlayerModal || _hud.ShowForgeModal || !this.IsNear(active.TraderNetworkId)))
        {
            trading.Close();
            active = null;
        }

        _hud.Trader = active;
        _shown = active is not null;
        if (captureOpen && active is null && frame >= 6 && frame % 10 == 6 &&
            this.TryFindNearest(_session.LocalPlayerInterpolatedPosition, WindowRadius, out ulong nearest))
        {
            _ = trading.TryOpen(nearest);
        }
    }

    /// <summary>Opens the nearest trader within talking distance (the interact key).</summary>
    public bool TryTalk() =>
        this.TryFindNearest(_session.LocalPlayerInterpolatedPosition, TalkRadius, out ulong trader) && _session.Trading.TryOpen(trader);

    /// <summary>Opens the trader under the cursor.</summary>
    public bool TryClick(Vector2 aimWorld) =>
        this.TryFindNearest(aimWorld, ClickRadius, out ulong trader) && _session.Trading.TryOpen(trader);

    /// <summary>
    /// Handles a click in the open window: a stock row buys 1 (Shift: 5), a bag item sells 1 (Shift: the whole stack), the
    /// X or a click outside closes it.
    /// </summary>
    public void HandleWindowInput(DesktopInputFrame input, int width, int height)
    {
        if (!input.LeftClickPressed || _hud.Trader is not TraderView trader)
        {
            return;
        }

        Vector2 mouse = input.MouseScreenPosition;
        if (_hud.TryHitTestModalCloseButton(mouse, width, height) || !_hud.IsPointInActiveModal(_session, mouse, width, height))
        {
            _hud.Trader = null;
            return;
        }

        int stockRow = TradeModalWidget.HitStockRow(mouse, trader, width, height);
        if (stockRow >= 0)
        {
            _ = _session.Trading.TryBuy((byte)stockRow, input.ShiftHeld ? BulkBuy : (ushort)1);
            return;
        }

        int bagSlot = TradeModalWidget.HitBagSlot(mouse, _session, width, height);
        if (bagSlot >= 0)
        {
            Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
            _session.CopyInventorySlots(slots);
            ushort quantity = input.ShiftHeld ? (ushort)Math.Max(1, slots[bagSlot].Quantity) : (ushort)1;
            _ = _session.Trading.TrySell((ushort)bagSlot, quantity);
        }
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

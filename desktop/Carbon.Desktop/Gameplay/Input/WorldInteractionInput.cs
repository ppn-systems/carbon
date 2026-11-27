// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Gameplay.Interaction;
using Carbon.Desktop.Gameplay.Trade;
using Carbon.Desktop.Input;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Graphics.Batching.Camera;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;

namespace Carbon.Desktop.Gameplay.Input;

/// <summary>
/// The world scene's input while no window is open (moved out of <c>WorldGameplayScene.Update</c>): clicks on the status
/// strip, hotbar and skill bar; a left click that talks to a trader or forge artisan, locks a monster and strikes; holding
/// the button to repeat strikes; right click to quick-move a hotbar slot or demolish; skill keys; the space strike ahead
/// of the player or at the locked target; and the interact, harvest, door, place and demolish keys.
/// </summary>
internal sealed class WorldInteractionInput
{
    private readonly IWorldSession _session;
    private readonly HudOverlayRenderer _hud;
    private readonly InventoryInputController _inventoryInput;
    private readonly InteractionController _interaction;
    private readonly TradeInteraction _trade;
    private readonly Camera2D _camera;
    private readonly Func<bool> _openForge;
    private Vector2 _lastMoveDirection = Vector2.UnitX;

    /// <summary>Initializes a new instance of the <see cref="WorldInteractionInput"/> class.</summary>
    /// <param name="session">World session.</param>
    /// <param name="hud">HUD renderer (hit tests).</param>
    /// <param name="inventoryInput">Inventory input, whose drag ends outside the inventory.</param>
    /// <param name="interaction">Interact and door keys.</param>
    /// <param name="trade">Trader clicks.</param>
    /// <param name="camera">World camera (cursor to world).</param>
    /// <param name="openForge">Opens the forge window; returns whether it opened.</param>
    public WorldInteractionInput(IWorldSession session, HudOverlayRenderer hud, InventoryInputController inventoryInput, InteractionController interaction, TradeInteraction trade, Camera2D camera, Func<bool> openForge)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
        _inventoryInput = inventoryInput ?? throw new ArgumentNullException(nameof(inventoryInput));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _trade = trade ?? throw new ArgumentNullException(nameof(trade));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _openForge = openForge ?? throw new ArgumentNullException(nameof(openForge));
    }

    /// <summary>Remembers the last walking direction, which the space strike aims along.</summary>
    /// <param name="move">This frame's movement input.</param>
    public void TrackMoveDirection(Vector2 move)
    {
        if (move.LengthSquared() > 1e-4f)
        {
            _lastMoveDirection = Vector2.Normalize(move);
        }
    }

    /// <summary>Handles one frame of world input; call only while no window is open.</summary>
    /// <param name="input">This frame's input.</param>
    /// <param name="mouseWorld">The cursor in world units.</param>
    /// <param name="width">Viewport width.</param>
    /// <param name="height">Viewport height.</param>
    public void Handle(in DesktopInputFrame input, Vector2 mouseWorld, int width, int height)
    {
        _inventoryInput.ResetDragState();

        if (input.LeftClickPressed)
        {
            Vector2 mouseScreen = input.MouseScreenPosition;
            if (_hud.TryHandleStatusStripClick(_session, mouseScreen, width))
            {
                // An icon opened its details, or a click outside closed them.
            }
            else if (_hud.TryHitTestHotbar(mouseScreen, width, height, out byte hotbarSlot))
            {
                _session.SelectHotbarSlot(hotbarSlot);
            }
            else if (_hud.TryHitTestSkill(_session, mouseScreen, width, height, out ushort skillFromClick))
            {
                _ = _session.TryCastSkill(skillFromClick, mouseWorld);
            }
            else
            {
                Vector2 aimWorld = _camera.ScreenToWorld(in mouseScreen);
                bool openedForgeNpc = _trade.TryClick(aimWorld);
                IReadOnlyList<RemoteEntityView> remotes = _session.RemoteEntities;
                for (int i = 0; !openedForgeNpc && i < remotes.Count; i++)
                {
                    RemoteEntityView entity = remotes[i];
                    if (entity.Kind == EntityKind.Npc &&
                        WorldActorCatalog.GetNpcSpec(entity.ArchetypeId).Capabilities.HasFlag(NpcCapabilities.ForgeArtisan) &&
                        Vector2.DistanceSquared(aimWorld, entity.Position) <= (24f * 24f) &&
                        _session.IsNearForgeStation())
                    {
                        openedForgeNpc = _openForge();
                        break;
                    }

                    if (entity.Kind != EntityKind.Monster || entity.CurrentHealth <= 0f)
                    {
                        continue;
                    }

                    float hitR = MathF.Max(22f, entity.Radius + 12f);
                    if (Vector2.DistanceSquared(aimWorld, entity.Position) <= hitR * hitR)
                    {
                        _session.SetLockedTarget(entity.NetworkId);
                        break;
                    }
                }

                if (!openedForgeNpc)
                {
                    _ = _session.TriggerPrimaryAction(aimWorld, out _);
                }
            }
        }
        else if (input.LeftMouseDown && !_session.CurrentAction.IsActive)
        {
            Vector2 mouseScreen = input.MouseScreenPosition;
            if (!_hud.IsOverStatusStrip(_session, mouseScreen, width) &&
                !_hud.TryHitTestHotbar(mouseScreen, width, height, out _) &&
                !_hud.TryHitTestSkill(_session, mouseScreen, width, height, out _))
            {
                Span<ItemSlot> curSlots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
                _session.CopyInventorySlots(curSlots);
                ItemSlot active = _session.SelectedHotbarSlot < curSlots.Length ? curSlots[_session.SelectedHotbarSlot] : ItemSlot.Empty;
                bool blockRepeat = !active.IsEmpty &&
                    _session.Items.Rules.Items.TryGet(active.ItemId, out ItemArchetype? activeDef) &&
                    activeDef.Category is ItemCategory.Consumable or ItemCategory.Placeable;
                if (!blockRepeat)
                {
                    Vector2 aimWorld = _camera.ScreenToWorld(in mouseScreen);
                    _ = _session.TriggerPrimaryAction(aimWorld, out _);
                }
            }
        }
        else if (input.RightClickPressed &&
                 _hud.TryHitTestHotbar(input.MouseScreenPosition, width, height, out byte quickSlot))
        {
            _ = _session.TryQuickMoveInventorySlot(quickSlot);
        }

        if (input.SkillPressed is ushort skillId)
        {
            _ = _session.TryCastSkill(skillId, mouseWorld);
        }

        if (input.SpaceActionPressed)
        {
            Vector2 spaceAim = _session.LocalPlayerInterpolatedPosition + (_lastMoveDirection * (_session.ChunkWorld.TileSize * 1.25f));
            if (_session.LockedTargetNetworkId != 0UL)
            {
                IReadOnlyList<RemoteEntityView> remotes = _session.RemoteEntities;
                for (int i = 0; i < remotes.Count; i++)
                {
                    if (remotes[i].NetworkId == _session.LockedTargetNetworkId)
                    {
                        spaceAim = remotes[i].Position;
                        Vector2 toTarget = spaceAim - _session.LocalPlayerInterpolatedPosition;
                        if (toTarget.LengthSquared() > 1e-4f)
                        {
                            _lastMoveDirection = Vector2.Normalize(toTarget);
                        }

                        break;
                    }
                }
            }

            _ = _session.TriggerPrimaryAction(spaceAim, out _);
        }

        if (input.InteractOrPickupPressed)
        {
            _ = _interaction.TryInteract(_openForge);
        }

        if (input.HarvestPressed)
        {
            _ = _session.TryHarvestNearbyProp(out _);
        }

        if (input.ToggleDoorPressed)
        {
            _ = _interaction.TryToggleNearestDoor();
        }

        if (input.PlaceStructurePressed)
        {
            _ = _session.TryPlaceStarterWall(out _);
        }

        if (input.DemolishStructurePressed ||
            (input.RightClickPressed &&
             !_hud.TryHitTestHotbar(input.MouseScreenPosition, width, height, out _)))
        {
            _ = _session.TryDemolishStructure(mouseWorld, out _);
        }
    }
}

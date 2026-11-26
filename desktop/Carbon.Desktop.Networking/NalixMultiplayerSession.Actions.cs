// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Entities.C2S;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Maps.C2S;
using Carbon.Desktop.Abstractions;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Props;
using Nalix.Abstractions.Networking.Datagrams;

namespace Carbon.Desktop.Networking;

public sealed partial class NalixMultiplayerSession
{
    /// <inheritdoc/>
    public void SelectHotbarSlot(byte slotIndex)
    {
        if (slotIndex >= 9)
        {
            return;
        }

        _selectedSlot = slotIndex;
        if (_tcpSession is { IsConnected: true })
        {
            _ = this.SendTcpPooledAsync<SelectHotbarSlot>(p => p.SlotIndex = slotIndex);
        }
    }

    /// <inheritdoc/>
    public bool TriggerPrimaryAction(Vector2 aimWorldPosition, out (int ChunkX, int ChunkY) invalidatedChunk)
    {
        invalidatedChunk = default;
        if (!this.IsReady || _tcpSession is not { IsConnected: true })
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        Vector2 delta = aimWorldPosition - pos;
        _lastAttackDir = delta.LengthSquared() > 1e-4f ? Vector2.Normalize(delta) : Vector2.UnitX;
        _localFacingDir = _lastAttackDir;

        ItemSlot activeSlot = _inventory.GetSlot(_selectedSlot);
        bool hasArchetype = StarterGameCatalog.Items.TryGet(activeSlot.ItemId, out ItemArchetype? archetype);
        ClientActionKind actionKind = ResolveActionKind(activeSlot.ItemId, archetype);
        float actionDuration = ResolveActionDuration(actionKind);
        _comboStep = (byte)(_comboStep ^ 1);
        _currentAction = new ClientActionState(
            actionKind,
            actionDuration,
            actionDuration,
            _lastAttackDir,
            activeSlot.ItemId,
            _comboStep);

        if (actionKind is ClientActionKind.SwordSlash or ClientActionKind.AxeChop or ClientActionKind.PickaxeMine)
        {
            _attackSwingTimer = actionDuration;
        }

        uint tick = _clientTick;
        byte slot = _selectedSlot;
        _ = this.SendGameplayDatagramOrTcpAsync<EntityAttackInput>(
            DeliveryMethod.ReliableUnordered,
            channel: 0,
            p =>
            {
                p.ClientTick = tick;
                p.TargetNetworkId = 0UL;
                p.AimX = aimWorldPosition.X;
                p.AimY = aimWorldPosition.Y;
                p.AbilitySlot = slot;
            });

        if (activeSlot.Quantity > 0 && hasArchetype && archetype is { Category: ItemCategory.Placeable or ItemCategory.Tool })
        {
            int aimTx = (int)MathF.Floor(aimWorldPosition.X / _tileSize);
            int aimTy = (int)MathF.Floor(aimWorldPosition.Y / _tileSize);
            invalidatedChunk = (Chunk2D.ToChunkCoord(aimTx), Chunk2D.ToChunkCoord(aimTy));
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool TryCraftRecipe(ushort recipeId)
    {
        if (!this.IsReady || _tcpSession is not { IsConnected: true } ||
            !StarterGameCatalog.Recipes.TryGet(recipeId, out CraftingRecipe? recipe) ||
            !RecipeMatcher.CanCraft(_inventory, recipe))
        {
            return false;
        }

        _ = this.SendTcpPooledAsync<CraftItem>(p =>
        {
            p.RecipeId = recipeId;
            p.Quantity = 1;
        });
        return true;
    }

    /// <inheritdoc/>
    public void DrainCombatEvents(List<ClientCombatEvent> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_worldGate)
        {
            for (int i = 0; i < _combatEvents.Count; i++)
            {
                destination.Add(_combatEvents[i]);
            }

            _combatEvents.Clear();
        }
    }

    /// <inheritdoc/>
    public void DrainPropImpactEvents(List<ClientPropImpactEvent> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_worldGate)
        {
            for (int i = 0; i < _propImpactEvents.Count; i++)
            {
                destination.Add(_propImpactEvents[i]);
            }

            _propImpactEvents.Clear();
        }
    }

    /// <inheritdoc/>
    public void DrainDirtyChunks(List<(int ChunkX, int ChunkY)> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_worldGate)
        {
            for (int i = 0; i < _dirtyChunks.Count; i++)
            {
                destination.Add(_dirtyChunks[i]);
            }

            _dirtyChunks.Clear();
        }
    }

    /// <inheritdoc/>
    public void DrainHudNotifications(List<string> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_worldGate)
        {
            for (int i = 0; i < _hudNotifications.Count; i++)
            {
                destination.Add(_hudNotifications[i]);
            }

            _hudNotifications.Clear();
        }
    }

    /// <inheritdoc/>
    public bool TryHarvestNearbyProp(out (int ChunkX, int ChunkY) modifiedChunk)
    {
        modifiedChunk = default;
        if (!this.IsReady || _tcpSession is not { IsConnected: true })
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        int ptx = (int)MathF.Floor(pos.X / _tileSize);
        int pty = (int)MathF.Floor(pos.Y / _tileSize);

        bool found = false;
        int bestWx = 0;
        int bestWy = 0;
        float bestDistSq = float.MaxValue;
        MapProp bestProp = default;

        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int wx = ptx + dx;
                int wy = pty + dy;
                if (_chunkWorld!.TryGetPropAt(wx, wy, out MapProp candidate) && !candidate.Destructibility.IsDestroyed)
                {
                    float distSq = Vector2.DistanceSquared(pos, candidate.WorldPosition);
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestWx = wx;
                        bestWy = wy;
                        bestProp = candidate;
                        found = true;
                    }
                }
            }
        }

        if (!found)
        {
            return false;
        }

        Vector2 toProp = bestProp.WorldPosition - pos;
        Vector2 aimDir = toProp.LengthSquared() > 1e-4f ? Vector2.Normalize(toProp) : _localFacingDir;
        _lastAttackDir = aimDir;
        _localFacingDir = aimDir;

        ClientActionKind harvestKind = bestProp.Category == PropCategory.Mineral
            ? ClientActionKind.PickaxeMine
            : ClientActionKind.AxeChop;
        const float harvestDuration = 0.30f;
        ItemSlot activeSlot = _inventory.GetSlot(_selectedSlot);
        _comboStep = (byte)(_comboStep ^ 1);
        _attackSwingTimer = harvestDuration;
        _currentAction = new ClientActionState(
            harvestKind,
            harvestDuration,
            harvestDuration,
            aimDir,
            activeSlot.ItemId,
            _comboStep);

        _ = this.SendTcpPooledAsync<MapPropHarvest>(p =>
        {
            p.TileX = bestWx;
            p.TileY = bestWy;
        });
        modifiedChunk = (Chunk2D.ToChunkCoord(bestWx), Chunk2D.ToChunkCoord(bestWy));
        return true;
    }

    /// <inheritdoc/>
    public bool TryPlaceTestHouse(out (int ChunkX, int ChunkY) centerChunk)
    {
        centerChunk = default;
        if (!this.IsReady || _tcpSession is not { IsConnected: true })
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        int ptx = (int)MathF.Floor(pos.X / _tileSize) + 2;
        int pty = (int)MathF.Floor(pos.Y / _tileSize);

        _ = this.SendTcpPooledAsync<MapStructurePlace>(p =>
        {
            p.TileX = ptx;
            p.TileY = pty;
            p.StructureType = (byte)StructureType.WoodWall;
        });

        centerChunk = (Chunk2D.ToChunkCoord(ptx), Chunk2D.ToChunkCoord(pty));
        return true;
    }

    /// <inheritdoc/>
    public bool TryToggleNearbyDoor(out (int ChunkX, int ChunkY) modifiedChunk)
    {
        modifiedChunk = default;
        if (!this.IsReady || _tcpSession is not { IsConnected: true })
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        int ptx = (int)MathF.Floor(pos.X / _tileSize);
        int pty = (int)MathF.Floor(pos.Y / _tileSize);

        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int wx = ptx + dx;
                int wy = pty + dy;
                if (_chunkWorld!.Buildings.TryGetStructure(wx, wy, out StructureTile st) && st.Category == StructureCategory.Door)
                {
                    _ = this.SendTcpPooledAsync<MapDoorToggle>(p =>
                    {
                        p.TileX = wx;
                        p.TileY = wy;
                    });
                    modifiedChunk = (Chunk2D.ToChunkCoord(wx), Chunk2D.ToChunkCoord(wy));
                    return true;
                }
            }
        }

        return false;
    }

    private void SendMoveInputAsync(Vector2 moveDir, bool sprint)
    {
        uint tick = _clientTick;
        _ = this.SendGameplayDatagramOrTcpAsync<EntityMoveInput>(
            DeliveryMethod.UnreliableSequenced,
            channel: 1,
            p =>
            {
                p.ClientTick = tick;
                p.DirectionX = moveDir.X;
                p.DirectionY = moveDir.Y;
                p.Sprint = sprint;
            });
    }

    private static ClientActionKind ResolveActionKind(uint itemId, ItemArchetype? archetype) => itemId switch
    {
        StarterGameCatalog.WoodAxe => ClientActionKind.AxeChop,
        StarterGameCatalog.StonePickaxe or StarterGameCatalog.CrystalPickaxe => ClientActionKind.PickaxeMine,
        _ when archetype is { Category: ItemCategory.Placeable } => ClientActionKind.PlaceStructure,
        _ when archetype is { Category: ItemCategory.Consumable } => ClientActionKind.ConsumeItem,
        _ => ClientActionKind.SwordSlash,
    };

    private static float ResolveActionDuration(ClientActionKind kind) => kind switch
    {
        ClientActionKind.AxeChop => 0.30f,
        ClientActionKind.PickaxeMine => 0.30f,
        ClientActionKind.ConsumeItem => 0.24f,
        ClientActionKind.PlaceStructure => 0.20f,
        ClientActionKind.SwordSlash or ClientActionKind.MonsterStrike or ClientActionKind.None or _ => 0.24f,
    };
}

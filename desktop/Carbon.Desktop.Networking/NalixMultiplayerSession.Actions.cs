// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Entities;
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
    public bool TryCastSkill(byte skillSlot, Vector2 aimWorldPosition)
    {
        if (!this.IsReady ||
            _tcpSession is not { IsConnected: true } ||
            !CharacterProgressionPolicy.TryGetSkillSpec(skillSlot, out string _, out float mpCost, out float stamCost, out float cdSec))
        {
            return false;
        }

        if (this.GetSkillCooldown(skillSlot) > 0.05f)
        {
            return false;
        }

        if (_localMana < mpCost || _localStamina < stamCost)
        {
            lock (_worldGate)
            {
                _hudNotifications.Add(_localMana < mpCost ? "Not enough Mana!" : "Not enough Stamina!");
            }

            return false;
        }

        _localMana = MathF.Max(0f, _localMana - mpCost);
        _localStamina = MathF.Max(0f, _localStamina - stamCost);
        switch (skillSlot)
        {
            case CharacterProgressionPolicy.SkillWhirlwind:
                _skill1Cd = cdSec;
                break;
            case CharacterProgressionPolicy.SkillArcaneNova:
                _skill2Cd = cdSec;
                break;
            case CharacterProgressionPolicy.SkillVitalSurge:
                _skill3Cd = cdSec;
                break;
            default:
                break;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        Vector2 delta = aimWorldPosition - pos;
        Vector2 aimDir = delta.LengthSquared() > 1e-4f ? Vector2.Normalize(delta) : _localFacingDir;
        _lastAttackDir = aimDir;
        _localFacingDir = aimDir;

        ClientActionKind actionKind = skillSlot switch
        {
            CharacterProgressionPolicy.SkillWhirlwind => ClientActionKind.SkillWhirlwind,
            CharacterProgressionPolicy.SkillArcaneNova => ClientActionKind.SkillArcaneNova,
            CharacterProgressionPolicy.SkillVitalSurge => ClientActionKind.SkillVitalSurge,
            _ => ClientActionKind.SwordSlash,
        };
        float duration = ResolveActionDuration(actionKind);
        _comboStep = (byte)(_comboStep ^ 1);
        _currentAction = new ClientActionState(
            actionKind,
            duration,
            duration,
            aimDir,
            _inventory.GetSlot(_selectedSlot).ItemId,
            _comboStep);
        if (skillSlot == CharacterProgressionPolicy.SkillWhirlwind)
        {
            _attackSwingTimer = duration;
        }

        uint tick = _clientTick;
        byte abilityCode = (byte)(100 + skillSlot);
        _ = this.SendGameplayDatagramOrTcpAsync<EntityAttackInput>(
            DeliveryMethod.ReliableUnordered,
            channel: 0,
            p =>
            {
                p.ClientTick = tick;
                p.TargetNetworkId = 0UL;
                p.AimX = aimWorldPosition.X;
                p.AimY = aimWorldPosition.Y;
                p.AbilitySlot = abilityCode;
            });
        return true;
    }

    /// <inheritdoc/>
    public bool TryMoveInventorySlot(ushort fromSlot, ushort toSlot)
    {
        if (!this.IsReady ||
            _tcpSession is not { IsConnected: true } ||
            fromSlot >= PlayerInventoryLayout.TotalCapacity ||
            toSlot >= PlayerInventoryLayout.TotalCapacity ||
            fromSlot == toSlot)
        {
            return false;
        }

        ItemSlot src = _inventory.GetSlot(fromSlot);
        ItemSlot dst = _inventory.GetSlot(toSlot);
        if (src.IsEmpty ||
            !PlayerInventoryLayout.CanAcceptSlotItem(toSlot, in src) ||
            !PlayerInventoryLayout.CanAcceptSlotItem(fromSlot, in dst))
        {
            return false;
        }

        ushort seq = ++_itemSeqId;
        _ = this.SendTcpPooledAsync<MoveInventorySlot>(p =>
        {
            p.FromSlot = fromSlot;
            p.ToSlot = toSlot;
            p.SequenceId = seq;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TryQuickMoveInventorySlot(ushort slotIndex)
    {
        if (!this.IsReady ||
            _tcpSession is not { IsConnected: true } ||
            slotIndex >= PlayerInventoryLayout.TotalCapacity)
        {
            return false;
        }

        ItemSlot source = _inventory.GetSlot(slotIndex);
        if (source.IsEmpty)
        {
            return false;
        }

        int dest = this.ResolveQuickMoveTargetSlot(slotIndex, in source);
        return dest >= 0 && this.TryMoveInventorySlot(slotIndex, (ushort)dest);
    }

    /// <inheritdoc/>
    public bool TryUpgradeInventoryItem(ushort slotIndex)
    {
        if (!this.IsReady ||
            _tcpSession is not { IsConnected: true } ||
            slotIndex >= PlayerInventoryLayout.TotalCapacity)
        {
            return false;
        }

        ItemSlot target = _inventory.GetSlot(slotIndex);
        if (!ItemEnhancementPolicy.CanUpgrade(in target, out uint m1, out int c1, out uint m2, out int c2, out float _))
        {
            return false;
        }

        if (_inventory.CountItem(m1) < c1 || (m2 != 0U && _inventory.CountItem(m2) < c2))
        {
            lock (_worldGate)
            {
                _hudNotifications.Add("Missing upgrade materials!");
            }

            return false;
        }

        ushort seq = ++_itemSeqId;
        _ = this.SendTcpPooledAsync<UpgradeInventoryItem>(p =>
        {
            p.SlotIndex = slotIndex;
            p.SequenceId = seq;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TryDropInventoryItem(ushort slotIndex, int quantity)
    {
        if (!this.IsReady ||
            _tcpSession is not { IsConnected: true } ||
            slotIndex >= PlayerInventoryLayout.TotalCapacity ||
            _inventory.GetSlot(slotIndex).IsEmpty)
        {
            return false;
        }

        ushort seq = ++_itemSeqId;
        _ = this.SendTcpPooledAsync<DropInventoryItem>(p =>
        {
            p.SlotIndex = slotIndex;
            p.Quantity = quantity;
            p.SequenceId = seq;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TryPickupNearbyLoot()
    {
        if (!this.IsReady || _tcpSession is not { IsConnected: true })
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        const float maxDistSq = 96f * 96f;
        ulong bestId = 0UL;
        float bestDistSq = maxDistSq;

        foreach (KeyValuePair<ulong, RemoteEntityState> kvp in _remoteEntities)
        {
            RemoteEntityState s = kvp.Value;
            if (s.Kind != EntityKind.LootDrop)
            {
                continue;
            }

            float d2 = Vector2.DistanceSquared(pos, s.Position);
            if (d2 <= bestDistSq)
            {
                bestDistSq = d2;
                bestId = s.NetworkId;
            }
        }

        if (bestId == 0UL)
        {
            return false;
        }

        _ = this.SendTcpPooledAsync<EntityPickupLoot>(p => p.LootNetworkId = bestId);
        return true;
    }

    private int ResolveQuickMoveTargetSlot(ushort slotIndex, in ItemSlot source)
    {
        if (slotIndex < PlayerInventoryLayout.BagLimit &&
            StarterGameCatalog.Items.TryGet(source.ItemId, out ItemArchetype? def))
        {
            int armorSlot = PlayerInventoryLayout.GetArmorSlotIndex(def.EquipSlot);
            if (armorSlot >= 0 && armorSlot < _inventory.Capacity)
            {
                return armorSlot;
            }
        }

        if (slotIndex >= PlayerInventoryLayout.BagLimit)
        {
            int bagSlot = this.FindOpenOrMergeSlot(in source, PlayerInventoryLayout.BackpackStart, Math.Min(PlayerInventoryLayout.BagLimit, _inventory.Capacity));
            if (bagSlot < 0)
            {
                bagSlot = this.FindOpenOrMergeSlot(in source, 0, Math.Min(PlayerInventoryLayout.HotbarCount, _inventory.Capacity));
            }

            return bagSlot;
        }

        int start = slotIndex < PlayerInventoryLayout.HotbarCount ? PlayerInventoryLayout.BackpackStart : 0;
        int end = slotIndex < PlayerInventoryLayout.HotbarCount
            ? Math.Min(PlayerInventoryLayout.BagLimit, _inventory.Capacity)
            : Math.Min(PlayerInventoryLayout.HotbarCount, _inventory.Capacity);
        return this.FindOpenOrMergeSlot(in source, start, end);
    }

    private int FindOpenOrMergeSlot(in ItemSlot source, int startInclusive, int endExclusive)
    {
        if (source.IsStackable)
        {
            int maxStack = StarterGameCatalog.GetMaxStackSize(source.ItemId);
            for (int i = startInclusive; i < endExclusive; i++)
            {
                ItemSlot candidate = _inventory.GetSlot(i);
                if (candidate.ItemId == source.ItemId && candidate.IsStackable && candidate.Quantity < maxStack)
                {
                    return i;
                }
            }
        }

        for (int i = startInclusive; i < endExclusive; i++)
        {
            if (_inventory.GetSlot(i).IsEmpty)
            {
                return i;
            }
        }

        return -1;
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

        ItemSlot activeSlot = _inventory.GetSlot(_selectedSlot);
        _ = StarterGameCatalog.Items.TryGet(activeSlot.ItemId, out ItemArchetype? activeArchetype);
        ClientActionKind harvestKind = ResolveActionKind(activeSlot.ItemId, activeArchetype);
        float harvestDuration = ResolveActionDuration(harvestKind);
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
        ClientActionKind.SkillWhirlwind => 0.42f,
        ClientActionKind.SkillArcaneNova => 0.45f,
        ClientActionKind.SkillVitalSurge => 0.48f,
        ClientActionKind.SwordSlash or ClientActionKind.MonsterStrike or ClientActionKind.None or _ => 0.24f,
    };
}

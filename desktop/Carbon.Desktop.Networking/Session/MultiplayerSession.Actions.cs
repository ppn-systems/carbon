// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Entities.C2S;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Carbon.Contracts.Maps.C2S;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Enhancement;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Props;
using Nalix.Abstractions.Networking.Datagrams;
using Nalix.Codec.Pooling;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Carbon.Desktop.Networking.Session;

public sealed partial class MultiplayerSession
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

        ItemSlot activeSlot;
        ulong targetNetId;
        lock (_worldGate)
        {
            activeSlot = _inventory.GetSlot(_selectedSlot);
            targetNetId = _lockedTargetNetworkId;
        }

        bool hasArchetype = _items.Rules.Items.TryGet(activeSlot.ItemId, out ItemArchetype? archetype);
        if (hasArchetype && archetype is { Category: ItemCategory.Consumable })
        {
            bool healsHp = archetype.HealHealth > 0f;
            bool healsMp = archetype.RestoreMana > 0f;
            bool healsStam = archetype.RestoreStamina > 0f;
            // Edible consumables also restore food, so only refuse when the hunger meter is (nearly) full too;
            // the server's overeating cap is 90 %.
            bool foodFull = _localFood >= _localMaxFood * 0.9f;
            if ((healsHp || healsMp || healsStam) &&
                foodFull &&
                (!healsHp || _localHealth >= _localMaxHealth - 0.5f) &&
                (!healsMp || _localMana >= _localMaxMana - 0.5f) &&
                (!healsStam || _stamina.Current >= _localMaxStamina - 0.5f))
            {
                lock (_worldGate)
                {
                    _hudNotifications.Add(GameText.Get("hud.vitals_full"));
                }

                return false;
            }
        }

        if (!(hasArchetype && archetype is { Category: ItemCategory.Placeable or ItemCategory.Consumable }) &&
            this.RefuseSwingWhileExhausted(hasArchetype ? archetype : null))
        {
            return false;
        }

        if (hasArchetype && archetype is { ProjectileSpeed: > 0f, AttackManaCost: > 0f } &&
            _localMana < archetype.AttackManaCost)
        {
            lock (_worldGate)
            {
                _hudNotifications.Add(GameText.Get("hud.not_enough_mana"));
            }

            return false;
        }

        ActionAnimationKind actionKind = ResolveActionKind(archetype);
        float actionDuration = ResolveActionDuration(actionKind);
        _comboStep = (byte)(_comboStep ^ 1);
        _currentAction = new ActionState(
            actionKind,
            actionDuration,
            actionDuration,
            _lastAttackDir,
            activeSlot.ItemId,
            _comboStep);

        if (actionKind is ActionAnimationKind.SwordSlash or ActionAnimationKind.AxeChop or ActionAnimationKind.PickaxeMine or ActionAnimationKind.BowShot or ActionAnimationKind.StaffCast)
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
                p.TargetNetworkId = targetNetId;
                p.AimX = aimWorldPosition.X;
                p.AimY = aimWorldPosition.Y;
                p.AbilitySlot = slot;
            });

        if (activeSlot.Quantity > 0 && hasArchetype && archetype is { Category: ItemCategory.Placeable or ItemCategory.Tool })
        {
            float distSq = Vector2.DistanceSquared(this.LocalPlayerInterpolatedPosition, aimWorldPosition);
            float maxDist = archetype.Category == ItemCategory.Placeable ? 3.5f * _tileSize : 56f;
            if (distSq <= (maxDist * maxDist))
            {
                int aimTx = (int)MathF.Floor(aimWorldPosition.X / _tileSize);
                int aimTy = (int)MathF.Floor(aimWorldPosition.Y / _tileSize);
                invalidatedChunk = (Chunk2D.ToChunkCoord(aimTx), Chunk2D.ToChunkCoord(aimTy));
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public bool TryCastSkill(ushort skillId, Vector2 aimWorldPosition)
    {
        if (!this.IsReady ||
            _tcpSession is not { IsConnected: true } ||
            !SkillCatalog.Skills.TryGet(skillId, out SkillDefinition? skillDef))
        {
            return false;
        }

        if (!SkillCatalog.CanCharacterAccessSkill(skillDef, _localClass, _localTraits, this.LocalUnlockedSkillMask))
        {
            return false;
        }

        byte rank = this.GetSkillRank(skillId);
        if (rank == 0)
        {
            lock (_worldGate)
            {
                _hudNotifications.Add(GameText.Get("hud.skill_locked"));
            }

            return false;
        }

        SkillSpec spec = skillDef.EvaluateRank(rank);
        if (this.GetSkillCooldown(skillId) > 0.05f)
        {
            return false;
        }

        if (_localMana < spec.ManaCost)
        {
            lock (_worldGate)
            {
                _hudNotifications.Add(GameText.Get("hud.not_enough_mana"));
            }

            return false;
        }

        if (!this.TryPredictStaminaSpend(spec.StaminaCost))
        {
            return false;
        }

        _localMana = MathF.Max(0f, _localMana - spec.ManaCost);
        lock (_worldGate)
        {
            _skillCooldowns[skillId] = spec.CooldownSeconds;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        Vector2 delta = aimWorldPosition - pos;
        Vector2 aimDir = delta.LengthSquared() > 1e-4f ? Vector2.Normalize(delta) : _localFacingDir;
        _lastAttackDir = aimDir;
        _localFacingDir = aimDir;

        ActionAnimationKind actionKind = MapSkillAnimKind(skillDef.AnimArchetype);
        float duration = ResolveActionDuration(actionKind);
        _comboStep = (byte)(_comboStep ^ 1);

        uint heldItemId;
        ulong targetNetId;
        lock (_worldGate)
        {
            heldItemId = _inventory.GetSlot(_selectedSlot).ItemId;
            targetNetId = _lockedTargetNetworkId;
        }

        _currentAction = new ActionState(
            actionKind,
            duration,
            duration,
            aimDir,
            heldItemId,
            _comboStep);
        if (actionKind == ActionAnimationKind.SpinSlash)
        {
            _attackSwingTimer = duration;
        }

        uint tick = _clientTick;
        byte abilityCode = AbilityInputEncoding.EncodeSkill(skillId);
        _ = this.SendGameplayDatagramOrTcpAsync<EntityAttackInput>(
            DeliveryMethod.ReliableUnordered,
            channel: 0,
            p =>
            {
                p.ClientTick = tick;
                p.TargetNetworkId = targetNetId;
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

        ushort expectedItemId;
        lock (_worldGate)
        {
            ItemSlot src = _inventory.GetSlot(fromSlot);
            ItemSlot dst = _inventory.GetSlot(toSlot);
            if (src.IsEmpty ||
                !PlayerInventoryLayout.CanAcceptSlotItem(_items.Rules, toSlot, in src) ||
                !PlayerInventoryLayout.CanAcceptSlotItem(_items.Rules, fromSlot, in dst))
            {
                return false;
            }

            expectedItemId = (ushort)src.ItemId;
        }

        ushort seq = ++_itemSeqId;
        _ = this.SendTcpPooledAsync<MoveInventorySlot>(p =>
        {
            p.FromSlot = fromSlot;
            p.ToSlot = toSlot;
            p.ExpectedItemId = expectedItemId;
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

        int dest;
        lock (_worldGate)
        {
            ItemSlot source = _inventory.GetSlot(slotIndex);
            if (source.IsEmpty)
            {
                return false;
            }

            dest = this.ResolveQuickMoveTargetSlot(slotIndex, in source);
        }

        return dest >= 0 && this.TryMoveInventorySlot(slotIndex, (ushort)dest);
    }

    /// <inheritdoc/>
    public bool TryUpgradeInventoryItem(ushort slotIndex, ushort protectionItemId = 0)
    {
        if (!this.IsReady ||
            _tcpSession is not { IsConnected: true } ||
            slotIndex >= PlayerInventoryLayout.TotalCapacity)
        {
            return false;
        }

        // A local pre-check only avoids pointless requests; the server re-checks every cost and rolls the outcome.
        string? missing = null;
        lock (_worldGate)
        {
            ItemRules rules = _items.Rules;
            ItemSlot target = _inventory.GetSlot(slotIndex);
            if (!ItemEnhancementPolicy.TryGetStep(rules, in target, out EnhancementStep? step))
            {
                return false;
            }

            ReadOnlySpan<RecipeIngredient> materials = step.MaterialSpan;
            for (int i = 0; i < materials.Length && missing is null; i++)
            {
                if (_inventory.CountItem(materials[i].ItemId) < materials[i].Quantity)
                {
                    missing = GameText.Get("forge.missing_materials");
                }
            }

            if (missing is null && Interlocked.Read(ref _localGold) < ItemEnhancementPolicy.GetGoldFee(rules, in target, step))
            {
                missing = GameText.Get("forge.not_enough_gold");
            }

            if (missing is null && protectionItemId != 0 && _inventory.CountItem(protectionItemId) < 1)
            {
                missing = GameText.Get("forge.no_scroll");
            }

            if (missing is not null)
            {
                _hudNotifications.Add(missing);
            }
        }

        if (missing is not null)
        {
            return false;
        }

        ushort seq = ++_itemSeqId;
        _ = this.ExecuteUpgradeRequestAsync(slotIndex, protectionItemId, seq);
        return true;
    }

    private async Task ExecuteUpgradeRequestAsync(ushort slotIndex, ushort protectionItemId, ushort seq)
    {
        TcpSession? tcp = _tcpSession;
        if (tcp is null || !tcp.IsConnected)
        {
            return;
        }

        try
        {
            RequestOptions reqOpts = RequestOptions.Default.WithTimeout(4000).WithEncrypt(tcp.State.EncryptionEnabled);
            using PacketScope<UpgradeInventoryItem> lease = PacketFactory<UpgradeInventoryItem>.Acquire();
            lease.Value.SlotIndex = slotIndex;
            lease.Value.ProtectionItemId = protectionItemId;
            lease.Value.SequenceId = seq;

            using EnhancementResult reply = await tcp.RequestAsync<EnhancementResult>(
                lease.Value,
                reqOpts,
                predicate: null,
                CancellationToken.None).ConfigureAwait(false);
            this.ApplyForgeResult(reply.Status, reply.Outcome, reply.ItemId, reply.NewLevel, reply.ProtectionConsumed);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Transient transport error ignored cleanly.
        }
    }

    /// <summary>Records a server forge result for the HUD (feedback pulse, outcome effect, notification).</summary>
    private void ApplyForgeResult(OpStatus status, EnhancementOutcome outcome, ushort itemId, byte newLevel, bool protectionConsumed)
    {
        lock (_worldGate)
        {
            if (status != OpStatus.Ok)
            {
                _hudNotifications.Add(
                    GameText.Get(status == OpStatus.Conflict ? "forge.missing_costs"
                    : status == OpStatus.OutOfRange ? "forge.out_of_range"
                    : "forge.refused"));
                return;
            }

            _forgeFeedbackTimer = 1.25f;
            _lastForgeSucceeded = outcome == EnhancementOutcome.Success;
            _lastForgeOutcome = outcome;
            _lastForgeNewLevel = newLevel;
            _ = Interlocked.Increment(ref _forgeResultSerial);

            string name = _items.GetDisplayName(itemId);
            (string note, string label) = outcome switch
            {
                EnhancementOutcome.Success => ("forge.note.success", "forge.label.success"),
                EnhancementOutcome.NoChange => (protectionConsumed ? "forge.note.no_change_guarded" : "forge.note.no_change", "forge.label.no_change"),
                EnhancementOutcome.Downgrade => ("forge.note.downgrade", "forge.label.downgrade"),
                EnhancementOutcome.Destroyed => ("forge.note.destroyed", "forge.label.destroyed"),
                EnhancementOutcome.None => (string.Empty, string.Empty),
                _ => (string.Empty, string.Empty),
            };

            if (note.Length == 0)
            {
                return;
            }

            TextArg itemArg = new("item", name);
            TextArg levelArg = new("level", newLevel);
            _hudNotifications.Add(GameText.Format(note, itemArg, levelArg));
            Vector2 pos = _simulation is not null && _simulation.Entities.IsAlive(_localPlayer)
                ? _simulation.GetInterpolatedPosition(_localPlayer) + _visualOffset
                : Vector2.Zero;
            _combatEvents.Add(new CombatFeedbackEvent(
                pos + new Vector2(0f, -16f),
                0f,
                IsCritical: outcome != EnhancementOutcome.NoChange,
                IsHeal: outcome == EnhancementOutcome.Success,
                Label: GameText.Format(label, levelArg)));
        }
    }

    /// <inheritdoc/>
    public bool TryDropInventoryItem(ushort slotIndex, int quantity)
    {
        if (!this.IsReady ||
            _tcpSession is not { IsConnected: true } ||
            slotIndex >= PlayerInventoryLayout.TotalCapacity)
        {
            return false;
        }

        ushort expectedItemId;
        lock (_worldGate)
        {
            ItemSlot slot = _inventory.GetSlot(slotIndex);
            if (slot.IsEmpty)
            {
                return false;
            }

            expectedItemId = (ushort)slot.ItemId;
        }

        ushort seq = ++_itemSeqId;
        _ = this.SendTcpPooledAsync<DropInventoryItem>(p =>
        {
            p.SlotIndex = slotIndex;
            p.Quantity = quantity;
            p.ExpectedItemId = expectedItemId;
            p.SequenceId = seq;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TrySplitInventorySlot(ushort fromSlot, int quantity)
    {
        if (!this.IsReady || _tcpSession is not { IsConnected: true } || fromSlot >= PlayerInventoryLayout.BagLimit)
        {
            return false;
        }

        ushort expectedItemId;
        int target;
        lock (_worldGate)
        {
            ItemSlot source = _inventory.GetSlot(fromSlot);
            if (source.IsEmpty || !source.IsStackable || quantity <= 0 || quantity >= source.Quantity)
            {
                return false;
            }

            Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
            _inventory.Slots.CopyTo(slots);
            target = InventoryActionPolicy.FindSplitTargetSlot(slots, fromSlot);
            expectedItemId = (ushort)source.ItemId;
            if (target < 0)
            {
                _hudNotifications.Add(GameText.Get("inventory.split.no_space"));
                return false;
            }
        }

        ushort seq = ++_itemSeqId;
        _ = this.SendTcpPooledAsync<SplitInventorySlot>(p =>
        {
            p.FromSlot = fromSlot;
            p.ToSlot = (ushort)target;
            p.Quantity = (ushort)quantity;
            p.ExpectedItemId = expectedItemId;
            p.SequenceId = seq;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TrySortInventory()
    {
        if (!this.IsReady || _tcpSession is not { IsConnected: true })
        {
            return false;
        }

        ushort seq = ++_itemSeqId;
        _ = this.SendTcpPooledAsync<SortInventory>(p => p.SequenceId = seq);
        return true;
    }

    /// <inheritdoc/>
    public bool TryDestroyInventoryItem(ushort slotIndex, int quantity)
    {
        if (!this.IsReady || _tcpSession is not { IsConnected: true } || slotIndex >= PlayerInventoryLayout.TotalCapacity)
        {
            return false;
        }

        ushort expectedItemId;
        lock (_worldGate)
        {
            ItemSlot slot = _inventory.GetSlot(slotIndex);
            if (slot.IsEmpty)
            {
                return false;
            }

            expectedItemId = (ushort)slot.ItemId;
        }

        ushort seq = ++_itemSeqId;
        _ = this.SendTcpPooledAsync<DestroyInventoryItem>(p =>
        {
            p.SlotIndex = slotIndex;
            p.Quantity = quantity;
            p.ExpectedItemId = expectedItemId;
            p.SequenceId = seq;
        });
        return true;
    }

    /// <inheritdoc/>
    public void SetLockedTarget(ulong networkId)
    {
        lock (_worldGate)
        {
            if (networkId == 0UL || !_remoteEntities.TryGetValue(networkId, out RemoteEntityState? target) || target.CurrentHealth <= 0f)
            {
                _lockedTargetNetworkId = 0UL;
                return;
            }

            _lockedTargetNetworkId = networkId;
        }
    }

    /// <inheritdoc/>
    public bool CycleNearestHostileTarget(float maxRange = 340f)
    {
        if (!this.IsReady)
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        float maxDistSq = maxRange * maxRange;
        List<(ulong NetworkId, float DistSq)> candidates = [];

        lock (_worldGate)
        {
            foreach (KeyValuePair<ulong, RemoteEntityState> kvp in _remoteEntities)
            {
                RemoteEntityState rs = kvp.Value;
                if (rs.Kind != EntityKind.Monster || rs.CurrentHealth <= 0f)
                {
                    continue;
                }

                float d2 = Vector2.DistanceSquared(pos, rs.Position);
                if (d2 <= maxDistSq)
                {
                    candidates.Add((rs.NetworkId, d2));
                }
            }

            if (candidates.Count == 0)
            {
                _lockedTargetNetworkId = 0UL;
                return false;
            }

            candidates.Sort(static (a, b) => a.DistSq.CompareTo(b.DistSq));
            int nextIndex = 0;
            if (_lockedTargetNetworkId != 0UL)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].NetworkId == _lockedTargetNetworkId)
                    {
                        nextIndex = (i + 1) % candidates.Count;
                        break;
                    }
                }
            }

            _lockedTargetNetworkId = candidates[nextIndex].NetworkId;
            return true;
        }
    }

    /// <inheritdoc/>
    public bool TryDemolishStructure(Vector2 aimWorldPosition, out (int ChunkX, int ChunkY) modifiedChunk)
    {
        modifiedChunk = default;
        if (!this.IsReady || _chunkWorld is null)
        {
            return false;
        }

        int tx = (int)MathF.Floor(aimWorldPosition.X / _tileSize);
        int ty = (int)MathF.Floor(aimWorldPosition.Y / _tileSize);
        bool hasStructure = _chunkWorld.Buildings.TryGetStructure(tx, ty, out StructureTile existing) &&
            existing.Type != StructureType.None;

        // A blueprint / frame (synced through chunk deltas) is cancelled by the same request.
        if (!hasStructure && !_chunkWorld.Construction.IsReserved(tx, ty))
        {
            return false;
        }

        modifiedChunk = (Chunk2D.ToChunkCoord(tx), Chunk2D.ToChunkCoord(ty));
        if (_tcpSession is { IsConnected: true })
        {
            _ = this.SendTcpPooledAsync<Carbon.Contracts.Maps.C2S.MapStructureDemolish>(p =>
            {
                p.TileX = tx;
                p.TileY = ty;
            });
            return true;
        }

        lock (_worldGate)
        {
            if (_chunkWorld.Buildings.TryDemolishStructure(tx, ty))
            {
                _dirtyChunks.Add(modifiedChunk);
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public bool TryAllocateCharacterPoint(PointAllocationKind kind, ushort targetId, byte amount = 1)
    {
        if (!this.IsReady || _tcpSession is not { IsConnected: true } || amount == 0)
        {
            return false;
        }

        _ = this.SendTcpPooledAsync<EntityAllocatePoint>(p =>
        {
            p.Kind = kind;
            p.TargetId = targetId;
            p.Amount = amount;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TryToggleRest()
    {
        if (!this.IsReady || _tcpSession is not { IsConnected: true })
        {
            return false;
        }

        bool rest = _localRestMode == RestMode.None;
        _ = this.SendTcpPooledAsync<EntityRestRequest>(p => p.Rest = rest);
        return true;
    }

    /// <inheritdoc/>
    public bool TryPickupLoot(ulong lootNetworkId)
    {
        if (lootNetworkId == 0UL || !this.IsReady || _tcpSession is not { IsConnected: true })
        {
            return false;
        }

        _ = this.SendTcpPooledAsync<EntityPickupLoot>(p => p.LootNetworkId = lootNetworkId);
        return true;
    }

    private int ResolveQuickMoveTargetSlot(ushort slotIndex, in ItemSlot source)
    {
        if (slotIndex < PlayerInventoryLayout.BagLimit &&
            _items.Rules.Items.TryGet(source.ItemId, out ItemArchetype? def))
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
            int maxStack = _items.GetMaxStackSize(source.ItemId);
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
    public bool TryCraftRecipe(ushort recipeId, ushort quantity = 1)
    {
        if (!this.IsReady ||
            _tcpSession is not { IsConnected: true } ||
            quantity == 0 ||
            quantity > 64 ||
            !_items.Rules.Recipes.TryGet(recipeId, out CraftingRecipe? recipe))
        {
            return false;
        }

        lock (_worldGate)
        {
            int bagCap = Math.Min(PlayerInventoryLayout.BagLimit, _inventory.Capacity);
            for (int rIdx = 0; rIdx < recipe.Ingredients.Count; rIdx++)
            {
                RecipeIngredient ing = recipe.Ingredients[rIdx];
                int needed = ing.Quantity * quantity;
                int avail = 0;
                for (int s = 0; s < bagCap; s++)
                {
                    ItemSlot cur = _inventory.GetSlot(s);
                    if (cur.ItemId == ing.ItemId && cur.Quantity > 0 && cur.UpgradeLevel == 0)
                    {
                        avail += cur.Quantity;
                    }
                }

                if (avail < needed)
                {
                    return false;
                }
            }
        }

        _ = this.SendTcpPooledAsync<CraftItem>(p =>
        {
            p.RecipeId = recipeId;
            p.Quantity = quantity;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TryVendorBuy(ushort itemId, ushort quantity = 1)
    {
        if (!this.IsReady || _tcpSession is not { IsConnected: true } || itemId == 0 || quantity == 0)
        {
            return false;
        }

        _ = this.SendTcpPooledAsync<VendorBuyItem>(p =>
        {
            p.ItemId = itemId;
            p.Quantity = quantity;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TryVendorSell(ushort slotIndex, ushort quantity = 1)
    {
        if (!this.IsReady || _tcpSession is not { IsConnected: true } || slotIndex >= PlayerInventoryLayout.BagLimit || quantity == 0)
        {
            return false;
        }

        _ = this.SendTcpPooledAsync<VendorSellItem>(p =>
        {
            p.SlotIndex = slotIndex;
            p.Quantity = quantity;
        });
        return true;
    }

    /// <inheritdoc/>
    public void DrainCombatEvents(List<CombatFeedbackEvent> destination)
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
    public void DrainPropImpactEvents(List<PropImpactEvent> destination)
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

        ItemSlot heldSlot;
        lock (_worldGate)
        {
            heldSlot = _inventory.GetSlot(_selectedSlot);
        }

        if (this.RefuseSwingWhileExhausted(_items.Rules.Items.TryGet(heldSlot.ItemId, out ItemArchetype? held) ? held : null))
        {
            return false;
        }

        Vector2 toProp = bestProp.WorldPosition - pos;
        Vector2 aimDir = toProp.LengthSquared() > 1e-4f ? Vector2.Normalize(toProp) : _localFacingDir;
        _lastAttackDir = aimDir;
        _localFacingDir = aimDir;

        ItemSlot activeSlot;
        lock (_worldGate)
        {
            activeSlot = _inventory.GetSlot(_selectedSlot);
        }

        _ = _items.Rules.Items.TryGet(activeSlot.ItemId, out ItemArchetype? activeArchetype);
        ActionAnimationKind harvestKind = ResolveActionKind(activeArchetype);
        float harvestDuration = ResolveActionDuration(harvestKind);
        _comboStep = (byte)(_comboStep ^ 1);
        _attackSwingTimer = harvestDuration;
        _currentAction = new ActionState(
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
    public bool TryPlaceStarterWall(out (int ChunkX, int ChunkY) centerChunk)
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
    public bool TryToggleDoor(int tileX, int tileY)
    {
        if (!this.IsReady || _tcpSession is not { IsConnected: true } ||
            !_chunkWorld!.Buildings.TryGetStructure(tileX, tileY, out StructureTile st) || st.Category != StructureCategory.Door)
        {
            return false;
        }

        _ = this.SendTcpPooledAsync<MapDoorToggle>(p =>
        {
            p.TileX = tileX;
            p.TileY = tileY;
        });
        return true;
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

    private static ActionAnimationKind ResolveActionKind(ItemArchetype? archetype)
    {
        if (archetype is null)
        {
            return ActionAnimationKind.SwordSlash;
        }

        if (archetype.Category == ItemCategory.Placeable)
        {
            return ActionAnimationKind.PlaceStructure;
        }

        if (archetype.Category == ItemCategory.Consumable)
        {
            return ActionAnimationKind.ConsumeItem;
        }

        if (archetype.Category == ItemCategory.Tool)
        {
            return archetype.Capabilities.HasFlag(ToolCapabilities.MineMineral)
                ? ActionAnimationKind.PickaxeMine
                : ActionAnimationKind.AxeChop;
        }

        if (archetype.Category == ItemCategory.Weapon && archetype.ProjectileSpeed > 0f)
        {
            return archetype.Capabilities.HasFlag(ToolCapabilities.MagicFocus)
                ? ActionAnimationKind.StaffCast
                : ActionAnimationKind.BowShot;
        }

        return ActionAnimationKind.SwordSlash;
    }

    private static ActionAnimationKind MapSkillAnimKind(SkillAnimArchetype anim) => anim switch
    {
        SkillAnimArchetype.SpinSlash => ActionAnimationKind.SpinSlash,
        SkillAnimArchetype.ArcaneWave => ActionAnimationKind.ArcaneWave,
        SkillAnimArchetype.SelfAura or _ => ActionAnimationKind.SelfAura,
    };

    private static float ResolveActionDuration(ActionAnimationKind kind) => kind switch
    {
        ActionAnimationKind.AxeChop => 0.30f,
        ActionAnimationKind.PickaxeMine => 0.30f,
        ActionAnimationKind.BowShot => 0.26f,
        ActionAnimationKind.StaffCast => 0.28f,
        ActionAnimationKind.ConsumeItem => 0.24f,
        ActionAnimationKind.PlaceStructure => 0.20f,
        ActionAnimationKind.SpinSlash => 0.42f,
        ActionAnimationKind.ArcaneWave => 0.45f,
        ActionAnimationKind.SelfAura => 0.48f,
        ActionAnimationKind.SwordSlash
            or ActionAnimationKind.MonsterStrike
            or ActionAnimationKind.MonsterBowShot
            or ActionAnimationKind.MonsterSpellCast
            or ActionAnimationKind.MonsterHeavySlam
            or ActionAnimationKind.None
            or _ => 0.24f,
    };
}

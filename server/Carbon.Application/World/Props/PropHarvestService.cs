// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Application.World.Construction;
using Carbon.Application.World.Loot;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using System.Collections.Generic;

namespace Carbon.Application.World.Props;

/// <summary>Wears the player's active tool by <paramref name="loss"/>; returns whether the slot changed.</summary>
internal delegate bool ToolWearHandler(PlayerState state, in ItemSlot activeSlot, float loss, out ItemSlot updatedSlot);

/// <summary>The result of one tool strike on a prop.</summary>
/// <param name="Damaged">Whether the prop took the hit (false: blocked, e.g. tool tier too low).</param>
/// <param name="Prop">The prop after the strike (the untouched prop when not damaged).</param>
/// <param name="Revision">The chunk's delta revision after the strike.</param>
/// <param name="Evaluation">How the tool fared against the prop.</param>
internal readonly record struct PropStrikeResult(bool Damaged, MapProp Prop, uint Revision, PropStrikeEvaluation Evaluation);

/// <summary>
/// Harvesting natural props with tools (audit SOLID-1, extracted from <see cref="AuthoritativeWorld"/>): a strike wears the
/// tool, damages the prop (respawning after 30 s), marks the chunk dirty and, when the prop breaks, drops its harvest and
/// grants experience. The tick's swing fallback also probes around the aim point, tells clients when a prop resisted the
/// tool, and lets tools damage structures. Called only under the world lock.
/// </summary>
internal sealed class PropHarvestService
{
    /// <summary>Reach of a tool swing, center of the player to center of the tile.</summary>
    public const float ToolHarvestRangePx = 52f;

    private readonly WorldSimulation _simulation;
    private readonly ChunkWorld2D _chunkWorld;
    private readonly float _tileSize;
    private readonly int _tickRate;
    private readonly Func<ItemRules> _itemRules;
    private readonly Func<ulong> _currentTick;
    private readonly GroundLootService _groundLoot;
    private readonly ConstructionService _construction;
    private readonly OutboundTickBuffer _outbound;
    private readonly HashSet<long> _dirtyChunks;
    private readonly Func<int, int, (int Offset, int Count)> _appendTileRecipients;
    private readonly Func<Entity, int, int, bool> _isWithinInteractRange;
    private readonly ToolWearHandler _wearTool;
    private readonly Action<PlayerState, uint> _grantHarvestExperience;

    /// <summary>Initializes a new instance of the <see cref="PropHarvestService"/> class.</summary>
    public PropHarvestService(
        WorldSimulation simulation,
        ChunkWorld2D chunkWorld,
        float tileSize,
        int tickRate,
        Func<ItemRules> itemRules,
        Func<ulong> currentTick,
        GroundLootService groundLoot,
        ConstructionService construction,
        OutboundTickBuffer outbound,
        HashSet<long> dirtyChunks,
        Func<int, int, (int Offset, int Count)> appendTileRecipients,
        Func<Entity, int, int, bool> isWithinInteractRange,
        ToolWearHandler wearTool,
        Action<PlayerState, uint> grantHarvestExperience)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _chunkWorld = chunkWorld ?? throw new ArgumentNullException(nameof(chunkWorld));
        _tileSize = tileSize;
        _tickRate = tickRate;
        _itemRules = itemRules ?? throw new ArgumentNullException(nameof(itemRules));
        _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
        _groundLoot = groundLoot ?? throw new ArgumentNullException(nameof(groundLoot));
        _construction = construction ?? throw new ArgumentNullException(nameof(construction));
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _dirtyChunks = dirtyChunks ?? throw new ArgumentNullException(nameof(dirtyChunks));
        _appendTileRecipients = appendTileRecipients ?? throw new ArgumentNullException(nameof(appendTileRecipients));
        _isWithinInteractRange = isWithinInteractRange ?? throw new ArgumentNullException(nameof(isWithinInteractRange));
        _wearTool = wearTool ?? throw new ArgumentNullException(nameof(wearTool));
        _grantHarvestExperience = grantHarvestExperience ?? throw new ArgumentNullException(nameof(grantHarvestExperience));
    }

    /// <summary>Gets whether the tile's center is within a tool swing of <paramref name="playerPos"/>.</summary>
    public bool IsWithinHarvestRange(Vector2 playerPos, int tileX, int tileY)
    {
        Vector2 tileCenter = new((tileX + 0.5f) * _tileSize, (tileY + 0.5f) * _tileSize);
        return Vector2.DistanceSquared(playerPos, tileCenter) <= (ToolHarvestRangePx * ToolHarvestRangePx);
    }

    /// <summary>Gets the delta revision of the chunk holding the tile (0 when unmodified).</summary>
    public uint ChunkRevision(int tileX, int tileY) =>
        _chunkWorld.TryGetDelta(Chunk2D.ToChunkCoord(tileX), Chunk2D.ToChunkCoord(tileY), out ChunkDelta? delta) && delta is not null
            ? delta.Revision
            : 0U;

    /// <summary>
    /// Strikes <paramref name="existing"/> at the tile with the player's active tool: wears the tool (queuing the slot
    /// change when <paramref name="replicateToolWear"/>), damages the prop, marks its chunk dirty and, when it breaks,
    /// drops its harvest and grants experience. <paramref name="toolWear"/> receives the worn slot, if any.
    /// </summary>
    public PropStrikeResult Strike(
        PlayerState state,
        int tileX,
        int tileY,
        in MapProp existing,
        in ItemSlot activeSlot,
        bool replicateToolWear,
        out ItemSlot? toolWear)
    {
        ArgumentNullException.ThrowIfNull(state);
        ItemRules rules = _itemRules();
        PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(
            rules,
            in activeSlot,
            ToToolTargetKind(existing.Category),
            existing.Destructibility.HarvestTier);

        toolWear = null;
        if (eval.DurabilityLoss > 0f && _wearTool(state, in activeSlot, eval.DurabilityLoss, out ItemSlot degradedSlot))
        {
            toolWear = degradedSlot;
            if (replicateToolWear)
            {
                _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, state.Session.SelectedHotbarSlot, degradedSlot));
            }
        }

        ulong respawnAt = _currentTick() + (ulong)(_tickRate * 30);
        if (!_chunkWorld.TryDamagePropAt(tileX, tileY, eval.Damage, eval.EffectiveTier, respawnAt, out MapProp updated))
        {
            return new PropStrikeResult(false, existing, this.ChunkRevision(tileX, tileY), eval);
        }

        _ = _dirtyChunks.Add(Chunk2D.PackKey(Chunk2D.ToChunkCoord(tileX), Chunk2D.ToChunkCoord(tileY)));
        uint revision = this.ChunkRevision(tileX, tileY);
        if (updated.Destructibility.IsDestroyed)
        {
            ItemSlot drop = PropHarvestCatalog.ResolveDrop(rules, updated.Type, out uint propXp);
            if (!drop.IsEmpty)
            {
                Vector2 propDropPos = new((tileX + 0.5f) * _tileSize, (tileY + 0.5f) * _tileSize);
                _groundLoot.Spawn(propDropPos, in drop);
            }

            _grantHarvestExperience(state, propXp > 0U ? propXp : 15U);
        }

        return new PropStrikeResult(true, updated, revision, eval);
    }

    /// <summary>
    /// Whether a swing from <paramref name="attackerPos"/> towards <paramref name="aimDir"/> reaches something
    /// <see cref="SwingAtWorld"/> would strike: a standing destructible prop on the aimed or probed tiles, or a structure on
    /// the aimed tile. Changes nothing (ADR-033 amendment: only a swing that connects costs stamina).
    /// </summary>
    public bool HasStrikeTarget(PlayerState state, Vector2 attackerPos, Vector2 aimDir)
    {
        ArgumentNullException.ThrowIfNull(state);
        int aimTx = (int)MathF.Floor(state.PendingAttackAim.X / _tileSize);
        int aimTy = (int)MathF.Floor(state.PendingAttackAim.Y / _tileSize);
        if (this.IsStrikeableProp(attackerPos, aimTx, aimTy))
        {
            return true;
        }

        Vector2 sideDir = new(-aimDir.Y, aimDir.X);
        ReadOnlySpan<Vector2> probes =
        [
            attackerPos + (aimDir * 24f),
            attackerPos + (((aimDir * 0.85f) + (sideDir * 0.52f)) * 24f),
            attackerPos + (((aimDir * 0.85f) - (sideDir * 0.52f)) * 24f),
        ];

        for (int i = 0; i < probes.Length; i++)
        {
            if (this.IsStrikeableProp(attackerPos, (int)MathF.Floor(probes[i].X / _tileSize), (int)MathF.Floor(probes[i].Y / _tileSize)))
            {
                return true;
            }
        }

        return _isWithinInteractRange(state.Entity, aimTx, aimTy) &&
            _chunkWorld.Buildings.TryGetStructure(aimTx, aimTy, out StructureTile structure) &&
            structure.Type != StructureType.None;
    }

    /// <summary>
    /// A swing that hit no monster: strikes the prop at the aim point, else the first prop in three probes ahead of the
    /// player. When a prop resists the tool, clients are told its unchanged durability. With no prop, a structure at the
    /// aim point within reach takes tool damage if the tool's tier allows.
    /// </summary>
    public void SwingAtWorld(PlayerState state, Vector2 attackerPos, Vector2 aimDir, in ItemSlot activeSlot)
    {
        ArgumentNullException.ThrowIfNull(state);
        int aimTx = (int)MathF.Floor(state.PendingAttackAim.X / _tileSize);
        int aimTy = (int)MathF.Floor(state.PendingAttackAim.Y / _tileSize);

        int blockedTx = 0;
        int blockedTy = 0;
        MapProp blockedProp = default;
        bool hasBlocked = false;

        if (this.IsWithinHarvestRange(attackerPos, aimTx, aimTy))
        {
            if (this.TryStrikeAndQueue(state, aimTx, aimTy, in activeSlot, ref blockedProp))
            {
                return;
            }

            if (blockedProp.Type != MapPropType.None)
            {
                blockedTx = aimTx;
                blockedTy = aimTy;
                hasBlocked = true;
            }
        }

        if (!hasBlocked)
        {
            Vector2 sideDir = new(-aimDir.Y, aimDir.X);
            ReadOnlySpan<Vector2> probes =
            [
                attackerPos + (aimDir * 24f),
                attackerPos + (((aimDir * 0.85f) + (sideDir * 0.52f)) * 24f),
                attackerPos + (((aimDir * 0.85f) - (sideDir * 0.52f)) * 24f),
            ];

            for (int i = 0; i < probes.Length; i++)
            {
                int wx = (int)MathF.Floor(probes[i].X / _tileSize);
                int wy = (int)MathF.Floor(probes[i].Y / _tileSize);
                if (!this.IsWithinHarvestRange(attackerPos, wx, wy))
                {
                    continue;
                }

                MapProp localBlocked = default;
                if (this.TryStrikeAndQueue(state, wx, wy, in activeSlot, ref localBlocked))
                {
                    return;
                }

                if (localBlocked.Type != MapPropType.None)
                {
                    blockedTx = wx;
                    blockedTy = wy;
                    blockedProp = localBlocked;
                    hasBlocked = true;
                    break;
                }
            }
        }

        if (hasBlocked)
        {
            this.QueuePropChange(blockedTx, blockedTy, this.ChunkRevision(blockedTx, blockedTy), blockedProp.Destructibility.CurrentDurability, isDestroyed: false);
            return;
        }

        if (_isWithinInteractRange(state.Entity, aimTx, aimTy) &&
            _chunkWorld.Buildings.TryGetStructure(aimTx, aimTy, out StructureTile targetStruct) &&
            targetStruct.Type != StructureType.None)
        {
            float dmg = 20f;
            byte toolTier = 1;
            if (!activeSlot.IsEmpty && _itemRules().Items.TryGet(activeSlot.ItemId, out ItemArchetype? toolDef))
            {
                toolTier = Math.Max((byte)1, toolDef.HarvestTier);
                bool isWoodStruct = targetStruct.Material == StructureMaterial.Wood;
                if (isWoodStruct && toolDef.Capabilities.HasFlag(ToolCapabilities.ChopFlora))
                {
                    dmg = MathF.Max(40f, toolDef.FloraHarvestDamage);
                }
                else if (!isWoodStruct && toolDef.Capabilities.HasFlag(ToolCapabilities.MineMineral))
                {
                    dmg = MathF.Max(45f, toolDef.MineralHarvestDamage);
                }
                else
                {
                    dmg = MathF.Max(15f, toolDef.BaseDamage * 0.7f);
                }
            }

            if (toolTier >= targetStruct.Destructibility.HarvestTier)
            {
                _ = _construction.DamageStructure(aimTx, aimTy, dmg);
            }
        }
    }

    private bool IsStrikeableProp(Vector2 attackerPos, int tileX, int tileY) =>
        this.IsWithinHarvestRange(attackerPos, tileX, tileY) &&
        _chunkWorld.TryGetPropAt(tileX, tileY, out MapProp prop) &&
        prop.Type != MapPropType.None &&
        !prop.Destructibility.IsDestroyed &&
        prop.Destructibility.IsDestructible;

    private static ToolTargetKind ToToolTargetKind(PropCategory category) =>
        category == PropCategory.Mineral
            ? ToolTargetKind.Mineral
            : ToolTargetKind.Flora;

    /// <summary>
    /// Strikes the harvestable prop on the tile and replicates the result. Returns <see langword="false"/> when there was
    /// none in reach or it resisted; a prop that resisted because of the tool is written to <paramref name="tierBlockedProp"/>.
    /// </summary>
    private bool TryStrikeAndQueue(PlayerState state, int wx, int wy, in ItemSlot activeSlot, ref MapProp tierBlockedProp)
    {
        if (!_simulation.Entities.IsAlive(state.Entity) ||
            !_simulation.Entities.HasTransform(state.Entity) ||
            !this.IsWithinHarvestRange(_simulation.Entities.GetTransform(state.Entity).Position, wx, wy))
        {
            return false;
        }

        if (!_chunkWorld.TryGetPropAt(wx, wy, out MapProp existing) ||
            existing.Type == MapPropType.None ||
            existing.Destructibility.IsDestroyed ||
            !existing.Destructibility.IsDestructible)
        {
            return false;
        }

        PropStrikeResult strike = this.Strike(state, wx, wy, in existing, in activeSlot, replicateToolWear: true, out _);
        if (!strike.Damaged)
        {
            if (strike.Evaluation.Outcome != ToolStrikeOutcome.Optimal || strike.Evaluation.EffectiveTier < existing.Destructibility.HarvestTier)
            {
                tierBlockedProp = existing;
            }

            return false;
        }

        this.QueuePropChange(wx, wy, strike.Revision, strike.Prop.Destructibility.CurrentDurability, strike.Prop.Destructibility.IsDestroyed);
        return true;
    }

    private void QueuePropChange(int tileX, int tileY, uint revision, float durability, bool isDestroyed)
    {
        (int offset, int count) = _appendTileRecipients(tileX, tileY);
        if (count > 0)
        {
            _outbound.Pending.PropChanges.Add(new OutboundPropChange(offset, count, tileX, tileY, revision, durability, isDestroyed));
        }
    }
}

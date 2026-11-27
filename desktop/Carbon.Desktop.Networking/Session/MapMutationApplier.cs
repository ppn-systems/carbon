// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Applies authoritative map mutations (prop durability, structure placement and door state) to the client world and
/// queues the feedback they cause: dirty chunks for re-baking, prop impact VFX, floating combat text and tool-tier HUD
/// warnings (audit round 2, SRP-1). The caller holds the session's world gate; the output lists are the session's own.
/// </summary>
internal sealed class MapMutationApplier
{
    private readonly List<(int ChunkX, int ChunkY)> _dirtyChunks;
    private readonly List<PropImpactEvent> _propImpactEvents;
    private readonly List<CombatFeedbackEvent> _combatEvents;
    private readonly List<string> _hudNotifications;

    /// <summary>Initializes a new instance of the <see cref="MapMutationApplier"/> class.</summary>
    /// <param name="dirtyChunks">Receives the chunk of every mutated tile.</param>
    /// <param name="propImpactEvents">Receives one impact event per prop state change.</param>
    /// <param name="combatEvents">Receives floating damage numbers and tool warnings.</param>
    /// <param name="hudNotifications">Receives tool-tier and tool-misuse warnings.</param>
    public MapMutationApplier(
        List<(int ChunkX, int ChunkY)> dirtyChunks,
        List<PropImpactEvent> propImpactEvents,
        List<CombatFeedbackEvent> combatEvents,
        List<string> hudNotifications)
    {
        _dirtyChunks = dirtyChunks;
        _propImpactEvents = propImpactEvents;
        _combatEvents = combatEvents;
        _hudNotifications = hudNotifications;
    }

    /// <summary>Applies a prop durability/destruction update and queues its impact feedback.</summary>
    /// <param name="world">The client world.</param>
    /// <param name="tileX">World tile X of the prop.</param>
    /// <param name="tileY">World tile Y of the prop.</param>
    /// <param name="durability">Authoritative remaining durability.</param>
    /// <param name="isDestroyed">Whether the prop was destroyed.</param>
    /// <param name="stage">The tree growth stage (ADR-040); a change to a stump is the tree being felled.</param>
    /// <param name="inventory">The local player's inventory (the held tool decides tier warnings).</param>
    /// <param name="selectedSlot">The selected hotbar slot.</param>
    /// <param name="items">The synced item catalog whose rules evaluate the strike.</param>
    public void ApplyPropChanged(
        ChunkWorld2D world,
        int tileX,
        int tileY,
        float durability,
        bool isDestroyed,
        TreeGrowthStage stage,
        InventoryContainer inventory,
        byte selectedSlot,
        SyncedItemCatalog items)
    {
        _dirtyChunks.Add((Chunk2D.ToChunkCoord(tileX), Chunk2D.ToChunkCoord(tileY)));

        if (world.TrySetPropStateAt(
            tileX,
            tileY,
            durability,
            isDestroyed,
            stage,
            out MapProp previous,
            out MapProp updated))
        {
            // ADR-040: a felled tree becomes a stump (it breaks like a destroyed prop); any other stage change is growth.
            bool felled = updated.GrowthStage == TreeGrowthStage.Stump && previous.GrowthStage != TreeGrowthStage.Stump;
            if (!felled && updated.GrowthStage != previous.GrowthStage)
            {
                return;
            }

            isDestroyed |= felled;
            float prevDur = previous.Destructibility.CurrentDurability;
            float nextDur = felled ? 0f : updated.Destructibility.CurrentDurability;
            float damageDealt = MathF.Max(0f, prevDur - nextDur);
            bool wasTierBlocked = !isDestroyed &&
                damageDealt <= 0.01f &&
                previous.Destructibility.IsDestructible;

            Vector2 worldPos = updated.WorldPosition;
            _propImpactEvents.Add(new PropImpactEvent(
                tileX,
                tileY,
                worldPos,
                updated.Type,
                updated.Category,
                damageDealt,
                nextDur,
                updated.Destructibility.MaxDurability,
                updated.Destructibility.HarvestTier,
                isDestroyed,
                wasTierBlocked));

            if (wasTierBlocked)
            {
                ItemSlot activeSlot = inventory.GetSlot(selectedSlot);
                ToolTargetKind kind = updated.Category == PropCategory.Mineral
                    ? ToolTargetKind.Mineral
                    : ToolTargetKind.Flora;
                PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(
                    items.Rules,
                    in activeSlot,
                    kind,
                    updated.Destructibility.HarvestTier);
                string warn = eval.Outcome != ToolStrikeOutcome.Optimal
                    ? ToolWarningText.Describe(eval.Outcome, updated.Destructibility.HarvestTier)
                    : ToolWarningText.Describe(ToolStrikeOutcome.InsufficientTier, updated.Destructibility.HarvestTier);
                _combatEvents.Add(new CombatFeedbackEvent(worldPos, 0f, IsCritical: false, IsHeal: false, Label: warn));
                _hudNotifications.Add(warn);
            }
            else if (damageDealt > 0.5f)
            {
                ItemSlot activeSlot = inventory.GetSlot(selectedSlot);
                ToolTargetKind kind = updated.Category == PropCategory.Mineral
                    ? ToolTargetKind.Mineral
                    : ToolTargetKind.Flora;
                PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(
                    items.Rules,
                    in activeSlot,
                    kind,
                    updated.Destructibility.HarvestTier);
                _combatEvents.Add(new CombatFeedbackEvent(
                    worldPos,
                    damageDealt,
                    IsCritical: isDestroyed,
                    IsHeal: false));
                if (eval.Outcome == ToolStrikeOutcome.SwordMisuseOnWood)
                {
                    _hudNotifications.Add(ToolWarningText.Describe(eval.Outcome, updated.Destructibility.HarvestTier));
                }
            }
        }
    }

    /// <summary>Applies a structure placement, demolition (<paramref name="structureType"/> 0) or door state.</summary>
    /// <param name="world">The client world.</param>
    /// <param name="tileX">World tile X of the structure.</param>
    /// <param name="tileY">World tile Y of the structure.</param>
    /// <param name="structureType">The <see cref="StructureType"/> now on the tile, or 0 when it was demolished.</param>
    /// <param name="doorState">The authoritative door state for a door structure.</param>
    public void ApplyStructureChanged(ChunkWorld2D world, int tileX, int tileY, byte structureType, byte doorState)
    {
        _dirtyChunks.Add((Chunk2D.ToChunkCoord(tileX), Chunk2D.ToChunkCoord(tileY)));

        if (structureType == 0)
        {
            _ = world.Buildings.TryDemolishStructure(tileX, tileY);
        }
        else
        {
            world.Buildings.ForcePlaceStructure(tileX, tileY, (StructureType)structureType);
            if (world.Buildings.TryGetStructure(tileX, tileY, out StructureTile placed) &&
                placed.Category == StructureCategory.Door &&
                (byte)placed.DoorState != doorState)
            {
                _ = world.Buildings.ToggleDoor(tileX, tileY, out _);
            }
        }
    }
}

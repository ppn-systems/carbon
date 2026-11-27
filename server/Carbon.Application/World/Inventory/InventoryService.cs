// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World.Loot;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Contracts.Common;
using Carbon.Economy;
using Carbon.Economy.Enhancement;
using Carbon.Economy.Repair;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Repair;
using Carbon.Maps.Building;
using Carbon.Primitives.Random;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Inventory;

/// <summary>
/// The inventory rules behind the player's inventory commands (audit SOLID-1, extracted from
/// <see cref="AuthoritativeWorld"/>): crafting at a station, moving/equipping, enhancing at a forge, dropping, and
/// ground-loot pickup, plus the station and harvest-tier lookups they need. Each mutation records the slots it changed
/// so the caller can replicate them; stats, experience, persistence and replies stay with the world. Called only under
/// the world lock.
/// </summary>
internal sealed class InventoryService
{
    private const int CraftingStationReachTiles = 5;

    private readonly WorldSimulation _simulation;
    private readonly BuildingGrid _buildings;
    private readonly float _tileSize;
    private readonly IGameplayEntropy _entropy;
    private readonly Func<ItemRules> _itemRules;
    private readonly Func<GameEconomy?> _economy;
    private readonly Func<ulong> _currentTick;
    private readonly Func<Vector2, float, NpcCapabilities, bool> _hasNearbyNpc;
    private readonly GroundLootService _groundLoot;
    private readonly OutboundTickBuffer _outbound;
    private readonly Action<PlayerState> _markInventoryDirty;
    private readonly List<ulong> _pickupScratch = new(32);
    private ulong _enhancementAttempts;

    /// <summary>Initializes a new instance of the <see cref="InventoryService"/> class.</summary>
    public InventoryService(
        WorldSimulation simulation,
        BuildingGrid buildings,
        float tileSize,
        IGameplayEntropy entropy,
        Func<ItemRules> itemRules,
        Func<GameEconomy?> economy,
        Func<ulong> currentTick,
        Func<Vector2, float, NpcCapabilities, bool> hasNearbyNpc,
        GroundLootService groundLoot,
        OutboundTickBuffer outbound,
        Action<PlayerState> markInventoryDirty)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
        _tileSize = tileSize;
        _entropy = entropy ?? throw new ArgumentNullException(nameof(entropy));
        _itemRules = itemRules ?? throw new ArgumentNullException(nameof(itemRules));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
        _hasNearbyNpc = hasNearbyNpc ?? throw new ArgumentNullException(nameof(hasNearbyNpc));
        _groundLoot = groundLoot ?? throw new ArgumentNullException(nameof(groundLoot));
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _markInventoryDirty = markInventoryDirty ?? throw new ArgumentNullException(nameof(markInventoryDirty));
    }

    /// <summary>Appends every slot that differs between <paramref name="before"/> and <paramref name="after"/>.</summary>
    public static void CollectModifiedSlots(
        ReadOnlySpan<ItemSlot> before,
        ReadOnlySpan<ItemSlot> after,
        List<(ushort SlotIndex, ItemSlot Slot)> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        for (int i = 0; i < after.Length; i++)
        {
            if (before[i] != after[i])
            {
                output.Add(((ushort)i, after[i]));
            }
        }
    }

    /// <summary>Gets whether <paramref name="modified"/> touches an equipment slot or the held hotbar slot (stats and look change).</summary>
    public static bool TouchesGear(List<(ushort SlotIndex, ItemSlot Slot)> modified, byte selectedHotbarSlot)
    {
        ArgumentNullException.ThrowIfNull(modified);
        for (int i = 0; i < modified.Count; i++)
        {
            if (modified[i].SlotIndex >= PlayerInventoryLayout.BagLimit || modified[i].SlotIndex == selectedHotbarSlot)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the harvest tier of the item in <paramref name="slotIndex"/> (0 for an empty slot or non-tool).</summary>
    public byte ResolveHarvestTier(InventoryContainer inventory, byte slotIndex)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (slotIndex < inventory.Capacity)
        {
            ItemSlot slot = inventory.GetSlot(slotIndex);
            if (!slot.IsEmpty && _itemRules().Items.TryGet(slot.ItemId, out ItemArchetype? def))
            {
                return def.HarvestTier;
            }
        }

        return 0;
    }

    /// <summary>
    /// Checks whether <paramref name="station"/> can be used at <paramref name="worldPos"/>: hand recipes anywhere; forge at
    /// an anvil or forge-artisan NPC; workbench at a built workbench; stove at a built stove or campfire; alchemy at a
    /// shrine keeper.
    /// </summary>
    public bool IsCraftingStationAvailable(CraftingStation station, Vector2 worldPos) => station switch
    {
        CraftingStation.None => true,
        CraftingStation.Forge => this.IsNearForgeStation(worldPos),
        CraftingStation.Workbench => this.IsNearStructure(worldPos, StructureType.Workbench, StructureType.Workbench),
        CraftingStation.Stove => this.IsNearStructure(worldPos, StructureType.Stove, StructureType.Campfire),
        CraftingStation.Alchemy => _hasNearbyNpc(worldPos, 175f * 175f, NpcCapabilities.Shrine),
        _ => false,
    };

    /// <summary>Gets whether a forge-artisan NPC or an intact forge anvil is within reach of <paramref name="worldPos"/>.</summary>
    public bool IsNearForgeStation(Vector2 worldPos)
    {
        if (_hasNearbyNpc(worldPos, 175f * 175f, NpcCapabilities.ForgeArtisan))
        {
            return true;
        }

        int ptx = (int)MathF.Floor(worldPos.X / _tileSize);
        int pty = (int)MathF.Floor(worldPos.Y / _tileSize);
        for (int dy = -5; dy <= 5; dy++)
        {
            for (int dx = -5; dx <= 5; dx++)
            {
                if (_buildings.TryGetStructure(ptx + dx, pty + dy, out StructureTile st) &&
                    st.Type == StructureType.ForgeAnvil &&
                    !st.IsDestroyed)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the tier of the <paramref name="venue"/> in reach of <paramref name="worldPos"/> (ADR-028): a blacksmith
    /// (forge-artisan NPC) for <see cref="RepairVenue.Blacksmith"/>; an intact forge anvil, else a workbench, for
    /// <see cref="RepairVenue.Station"/>. Reach and tiers come from <c>repair.json → stations</c>. <c>0</c> when none is in reach.
    /// </summary>
    public int GetRepairStationTier(Vector2 worldPos, RepairVenue venue, RepairTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        float reach = table.BlacksmithReach;
        return venue switch
        {
            RepairVenue.Blacksmith => _hasNearbyNpc(worldPos, reach * reach, NpcCapabilities.ForgeArtisan) ? table.BlacksmithTier : 0,
            RepairVenue.Station when this.IsNearStructure(worldPos, StructureType.ForgeAnvil, table.StationReachTiles) => table.ForgeAnvilTier,
            RepairVenue.Station when this.IsNearStructure(worldPos, StructureType.Workbench, table.StationReachTiles) => table.WorkbenchTier,
            RepairVenue.None or RepairVenue.Station or RepairVenue.Blacksmith => 0,
            _ => 0,
        };
    }

    /// <summary>
    /// Repairs ONE item (ADR-028): finds the venue's tier in reach, then <see cref="RepairWorkshop"/> checks the item,
    /// tier, materials and fee and spends them exactly once. Records the changed slots in <paramref name="modified"/> and
    /// stages the wallet sync when a fee was paid. On refusal nothing changed.
    /// </summary>
    public RepairAttempt Repair(
        PlayerState state,
        ushort slotIndex,
        ushort expectedItemId,
        byte steps,
        RepairVenue venue,
        Vector2 playerPos,
        List<(ushort SlotIndex, ItemSlot Slot)> modified)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(modified);
        modified.Clear();
        if (slotIndex >= state.Inventory.Capacity)
        {
            return RepairAttempt.Refused(RepairError.InvalidRequest);
        }

        RepairTable table = _itemRules().Repair;
        ReadOnlySpan<ItemSlot> before = state.Inventory.Slots;
        Span<ItemSlot> beforeSnap = stackalloc ItemSlot[before.Length];
        before.CopyTo(beforeSnap);

        ulong tick = _currentTick();
        int tier = this.GetRepairStationTier(playerPos, venue, table);
        RepairAttempt attempt = RepairWorkshop.TryRepair(table, state.Wallet, state.Inventory, slotIndex, expectedItemId, steps, venue, tier, tick);
        if (!attempt.IsOk)
        {
            return attempt;
        }

        if (attempt.GoldSpent > 0)
        {
            _outbound.Pending.WalletSyncs.Add(new OutboundWalletSync(state.Session.ConnectionId, state.Wallet.Gold, state.Wallet.Diamonds, state.Wallet.Sequence));
        }

        CollectModifiedSlots(beforeSnap, state.Inventory.Slots, modified);
        return attempt;
    }

    /// <summary>
    /// Crafts <paramref name="recipe"/> up to <paramref name="quantity"/> times into the bag (through the economy checkout
    /// when there is one). Fails with <see cref="OpStatus.OutOfRange"/> away from its station, or with the first craft's
    /// failure when nothing could be crafted.
    /// </summary>
    public OpStatus Craft(
        PlayerState state,
        CraftingRecipe recipe,
        ushort quantity,
        Vector2 playerPos,
        List<(ushort SlotIndex, ItemSlot Slot)> modified,
        out int crafted)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(modified);
        crafted = 0;

        // recipes.json rules: the station must be nearby; character level gates both unlock and skill (no crafting
        // skills are tracked yet, so the character level stands in for the recipe's skill level).
        if (!this.IsCraftingStationAvailable(recipe.Station, playerPos))
        {
            return OpStatus.OutOfRange;
        }

        ItemRules rules = _itemRules();
        GameEconomy? economy = _economy();
        CraftContext context = new(recipe.Station, state.Level, state.Level);
        int bagLimit = Math.Min(PlayerInventoryLayout.BagLimit, state.Inventory.Capacity);
        modified.Clear();
        ReadOnlySpan<ItemSlot> beforeSlots = state.Inventory.Slots;
        Span<ItemSlot> beforeSnapshot = stackalloc ItemSlot[beforeSlots.Length];
        beforeSlots.CopyTo(beforeSnapshot);

        CraftResult firstFailure = CraftResult.Ok;
        for (int q = 0; q < quantity; q++)
        {
            CraftResult result = economy is not null
                ? economy.Checkout.TryCraft(state.Wallet, state.Inventory, recipe, context, _currentTick(), bagLimit)
                : RecipeMatcher.TryCraft(state.Inventory, recipe, rules.ItemCatalog, context, bagLimit);
            if (!result.IsOk)
            {
                firstFailure = result;
                break;
            }

            crafted++;
        }

        if (crafted == 0)
        {
            return firstFailure.Status;
        }

        CollectModifiedSlots(beforeSnapshot, state.Inventory.Slots, modified);
        return OpStatus.Ok;
    }

    /// <summary>
    /// Runs one slot command (move, drop, split, destroy, sort) and records the changed slots in <paramref name="modified"/>.
    /// A command whose <see cref="InventorySlotCommand.ExpectedItemId"/> no longer matches its source slot is
    /// <see cref="OpStatus.Conflict"/> and changes nothing (ADR-020 D2).
    /// </summary>
    public OpStatus Execute(PlayerState state, in InventorySlotCommand command, Vector2 playerPos, List<(ushort SlotIndex, ItemSlot Slot)> modified)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(modified);
        modified.Clear();
        if (command.Kind != InventorySlotCommandKind.Sort && command.SlotA < state.Inventory.Capacity)
        {
            ItemSlot source = state.Inventory.GetSlot(command.SlotA);
            if (command.ExpectedItemId != 0 && !source.IsEmpty && source.ItemId != command.ExpectedItemId)
            {
                return OpStatus.Conflict;
            }
        }

        return command.Kind switch
        {
            InventorySlotCommandKind.Move => this.Move(state, command.SlotA, command.SlotB, modified),
            InventorySlotCommandKind.Drop => this.Drop(state, command.SlotA, command.Quantity, playerPos, modified),
            InventorySlotCommandKind.Split => this.Split(state, command.SlotA, command.SlotB, command.Quantity, modified),
            InventorySlotCommandKind.Destroy => this.Destroy(state, command.SlotA, command.Quantity, modified),
            InventorySlotCommandKind.Sort => this.Sort(state, modified),
            _ => OpStatus.Invalid,
        };
    }

    /// <summary>Moves part of a bag stack into an empty bag slot or onto a matching stack with room (ADR-020).</summary>
    public OpStatus Split(PlayerState state, ushort fromSlot, ushort toSlot, int quantity, List<(ushort SlotIndex, ItemSlot Slot)> modified)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(modified);
        if (fromSlot >= state.Inventory.Capacity || toSlot >= state.Inventory.Capacity)
        {
            return OpStatus.Invalid;
        }

        if (state.Inventory.GetSlot(fromSlot).IsEmpty)
        {
            return OpStatus.NotFound;
        }

        modified.Clear();
        if (!PlayerInventoryLayout.TrySplitStack(_itemRules(), state.Inventory, fromSlot, toSlot, quantity))
        {
            return OpStatus.Invalid;
        }

        modified.Add((fromSlot, state.Inventory.GetSlot(fromSlot)));
        modified.Add((toSlot, state.Inventory.GetSlot(toSlot)));
        return OpStatus.Ok;
    }

    /// <summary>Merges partial stacks and sorts the backpack (<c>9..35</c>); the hotbar and equipment are untouched (ADR-020).</summary>
    public OpStatus Sort(PlayerState state, List<(ushort SlotIndex, ItemSlot Slot)> modified)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(modified);
        modified.Clear();
        ReadOnlySpan<ItemSlot> before = state.Inventory.Slots;
        Span<ItemSlot> beforeSnap = stackalloc ItemSlot[before.Length];
        before.CopyTo(beforeSnap);
        if (InventorySorting.SortBackpack(_itemRules(), state.Inventory))
        {
            CollectModifiedSlots(beforeSnap, state.Inventory.Slots, modified);
        }

        return OpStatus.Ok;
    }

    /// <summary>Destroys all of a slot (or <paramref name="quantity"/> of it) permanently; nothing reaches the ground (ADR-020).</summary>
    public OpStatus Destroy(PlayerState state, ushort slotIndex, int quantity, List<(ushort SlotIndex, ItemSlot Slot)> modified)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(modified);
        if (slotIndex >= state.Inventory.Capacity)
        {
            return OpStatus.Invalid;
        }

        ItemSlot slot = state.Inventory.GetSlot(slotIndex);
        if (slot.IsEmpty)
        {
            return OpStatus.NotFound;
        }

        int destroyQty = quantity <= 0 || quantity >= slot.Quantity ? slot.Quantity : quantity;
        ItemSlot remainder = slot.Quantity > destroyQty ? slot with { Quantity = slot.Quantity - destroyQty } : ItemSlot.Empty;
        state.Inventory.SetSlot(slotIndex, in remainder);
        modified.Clear();
        modified.Add((slotIndex, remainder));
        return OpStatus.Ok;
    }

    /// <summary>Moves, swaps, merges or equips a slot, enforcing armor slot constraints.</summary>
    public OpStatus Move(PlayerState state, ushort fromSlot, ushort toSlot, List<(ushort SlotIndex, ItemSlot Slot)> modified)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(modified);
        if (fromSlot >= state.Inventory.Capacity || toSlot >= state.Inventory.Capacity)
        {
            return OpStatus.Invalid;
        }

        modified.Clear();
        ReadOnlySpan<ItemSlot> before = state.Inventory.Slots;
        Span<ItemSlot> beforeSnap = stackalloc ItemSlot[before.Length];
        before.CopyTo(beforeSnap);

        if (!PlayerInventoryLayout.TryMoveOrEquipSlot(_itemRules(), state.Inventory, fromSlot, toSlot))
        {
            return OpStatus.Invalid;
        }

        CollectModifiedSlots(beforeSnap, state.Inventory.Slots, modified);
        return OpStatus.Ok;
    }

    /// <summary>
    /// Runs one forge enhancement attempt on the item in <paramref name="slotIndex"/> (ADR-024): requires a forge in reach,
    /// then <see cref="ForgeEnhancer"/> checks every cost, spends the gold fee, materials and the optional protection
    /// scroll exactly once, rolls on the server's deterministic stream and writes the outcome. Returns
    /// <see cref="OpStatus.Ok"/> once the attempt ran (<paramref name="attempt"/> tells the outcome;
    /// <paramref name="experience"/> is what a success earns), or the reason nothing was spent.
    /// <paramref name="deterministicRollSeed"/> (tests) is mixed into the roll stream when non-zero. Stages the wallet sync
    /// and, for notable outcomes, a world-wide announcement to <paramref name="worldConnections"/> for the tick dispatch.
    /// </summary>
    public OpStatus Upgrade(
        PlayerState state,
        ushort slotIndex,
        ushort protectionItemId,
        Vector2 playerPos,
        uint deterministicRollSeed,
        Dictionary<ulong, PlayerState>.KeyCollection worldConnections,
        List<(ushort SlotIndex, ItemSlot Slot)> modified,
        out ForgeAttempt attempt,
        out uint experience)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(modified);
        attempt = ForgeAttempt.Rejected(OpStatus.Invalid);
        experience = 0U;
        if (!this.IsNearForgeStation(playerPos))
        {
            attempt = ForgeAttempt.Rejected(OpStatus.OutOfRange);
            return OpStatus.OutOfRange;
        }

        if (slotIndex >= state.Inventory.Capacity)
        {
            return OpStatus.Invalid;
        }

        modified.Clear();
        ReadOnlySpan<ItemSlot> before = state.Inventory.Slots;
        Span<ItemSlot> beforeSnap = stackalloc ItemSlot[before.Length];
        before.CopyTo(beforeSnap);

        ulong tick = _currentTick();
        ulong nonce = ++_enhancementAttempts ^ ((ulong)deterministicRollSeed << 32);
        DeterministicRandom random = ForgeEnhancer.CreateRandom(_entropy.KeyAt(tick), tick, state.Session.PlayerNetworkId, nonce);
        attempt = ForgeEnhancer.TryEnhance(_itemRules(), state.Wallet, state.Inventory, slotIndex, protectionItemId, ref random, tick);
        if (attempt.Status != OpStatus.Ok)
        {
            return attempt.Status;
        }

        if (attempt.Outcome == EnhancementOutcome.Success)
        {
            experience = (uint)(20 + (attempt.NewLevel * 5));
        }

        OutboundBatch pending = _outbound.Pending;
        if (attempt.GoldSpent > 0)
        {
            pending.WalletSyncs.Add(new OutboundWalletSync(state.Session.ConnectionId, state.Wallet.Gold, state.Wallet.Diamonds, state.Wallet.Sequence));
        }

        if (_itemRules().Enhancement.IsAnnounced(attempt.Outcome, attempt.NewLevel))
        {
            int offset = pending.Recipients.Count;
            foreach (ulong connection in worldConnections)
            {
                pending.Recipients.Add(connection);
            }

            byte level = attempt.IsDestroyed ? attempt.PreviousLevel : attempt.NewLevel;
            pending.EnhancementAnnouncements.Add(new OutboundEnhancementAnnouncement(
                offset, pending.Recipients.Count - offset, state.Session.PlayerNetworkId, state.SourceCharacter.Name, (ushort)attempt.ItemId, attempt.Outcome, level));
        }

        CollectModifiedSlots(beforeSnap, state.Inventory.Slots, modified);
        return OpStatus.Ok;
    }

    /// <summary>
    /// Drops all of a slot (or <paramref name="quantity"/> of it) as ground loot just beside the player; the player's own
    /// auto-collect ignores it until they walk away (they can still pick it up with the interact key).
    /// </summary>
    public OpStatus Drop(PlayerState state, ushort slotIndex, int quantity, Vector2 playerPos, List<(ushort SlotIndex, ItemSlot Slot)> modified)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(modified);
        if (slotIndex >= state.Inventory.Capacity)
        {
            return OpStatus.Invalid;
        }

        ItemSlot slot = state.Inventory.GetSlot(slotIndex);
        if (slot.IsEmpty)
        {
            return OpStatus.NotFound;
        }

        int dropQty = quantity <= 0 || quantity >= slot.Quantity ? slot.Quantity : quantity;
        ItemSlot dropStack = slot with { Quantity = dropQty };
        ItemSlot remainder = slot.Quantity > dropQty ? slot with { Quantity = slot.Quantity - dropQty } : ItemSlot.Empty;
        state.Inventory.SetSlot(slotIndex, in remainder);

        modified.Clear();
        modified.Add((slotIndex, remainder));
        _groundLoot.Spawn(playerPos + new Vector2(28f, 6f), in dropStack, state.Session.CharacterId);
        return OpStatus.Ok;
    }

    /// <summary>Picks up every ground-loot stack within auto-pickup range of <paramref name="playerPos"/> that fits.</summary>
    public void CollectNearbyGroundLoot(PlayerState state, Vector2 playerPos)
    {
        if (_groundLoot.Count == 0)
        {
            return;
        }

        _pickupScratch.Clear();
        _groundLoot.CollectAutoPickupCandidates(playerPos, state.Session.CharacterId, _pickupScratch);
        for (int i = 0; i < _pickupScratch.Count; i++)
        {
            _ = this.TryPickup(state, _pickupScratch[i], 36f * 36f);
        }
    }

    /// <summary>
    /// Moves as much of the ground-loot stack <paramref name="lootNetId"/> as fits into the player's bag when it lies within
    /// <paramref name="maxRangeSq"/>; replicates the changed slots. Returns <see langword="false"/> when nothing was taken.
    /// </summary>
    public bool TryPickup(PlayerState state, ulong lootNetId, float maxRangeSq)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!_groundLoot.TryGet(lootNetId, out GroundLootEntry meta) ||
            !_simulation.TryGetEntityByNetworkId(lootNetId, out Entity lootEnt) ||
            !_simulation.Entities.IsAlive(lootEnt) ||
            !_simulation.Entities.HasTransform(lootEnt) ||
            !_simulation.Entities.HasTransform(state.Entity))
        {
            return false;
        }

        Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
        Vector2 lootPos = _simulation.Entities.GetTransform(lootEnt).Position;
        if (Vector2.DistanceSquared(playerPos, lootPos) > maxRangeSq ||
            !meta.CanBePickedUpBy(state.Session.CharacterId, _currentTick()))
        {
            return false;
        }

        if (meta.IsCurrency)
        {
            return this.TryPickupCurrency(state, lootNetId, lootEnt, lootPos, in meta);
        }

        ItemSlot metaItem = meta.Item;
        int maxStack = _itemRules().GetMaxStackSize(metaItem.ItemId);
        ReadOnlySpan<ItemSlot> before = state.Inventory.Slots;
        Span<ItemSlot> snapshot = stackalloc ItemSlot[before.Length];
        before.CopyTo(snapshot);

        int remainder = PlayerInventoryLayout.TryAddBagItem(state.Inventory, in metaItem, maxStack);
        if (remainder >= metaItem.Quantity)
        {
            return false;
        }

        ReadOnlySpan<ItemSlot> after = state.Inventory.Slots;
        for (int i = 0; i < after.Length; i++)
        {
            if (snapshot[i] != after[i])
            {
                _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, (ushort)i, after[i]));
            }
        }

        _markInventoryDirty(state);
        _groundLoot.ApplyPickup(lootNetId, lootEnt, lootPos, in meta, remainder);
        return true;
    }

    /// <summary>
    /// Credits a gold or diamond pile to the wallet, persists it and syncs the balance. A pile the wallet cannot take whole
    /// (at its cap) stays on the ground.
    /// </summary>
    private bool TryPickupCurrency(PlayerState state, ulong lootNetId, Entity lootEnt, Vector2 lootPos, in GroundLootEntry meta)
    {
        if (!state.Wallet.TryCredit(meta.Currency, meta.CurrencyAmount, meta.CreditReason, _currentTick()).IsOk)
        {
            return false;
        }

        _outbound.Pending.WalletSyncs.Add(new OutboundWalletSync(state.Session.ConnectionId, state.Wallet.Gold, state.Wallet.Diamonds, state.Wallet.Sequence));
        _markInventoryDirty(state); // marks the character dirty, so the new balance is saved with it (ADR-012 D2)
        _groundLoot.ApplyPickup(lootNetId, lootEnt, lootPos, in meta, 0);
        return true;
    }

    private bool IsNearStructure(Vector2 worldPos, StructureType first, StructureType second) =>
        this.IsNearStructure(worldPos, first, second, CraftingStationReachTiles);

    private bool IsNearStructure(Vector2 worldPos, StructureType type, int reachTiles) =>
        this.IsNearStructure(worldPos, type, type, reachTiles);

    private bool IsNearStructure(Vector2 worldPos, StructureType first, StructureType second, int reachTiles)
    {
        int ptx = (int)MathF.Floor(worldPos.X / _tileSize);
        int pty = (int)MathF.Floor(worldPos.Y / _tileSize);
        for (int dy = -reachTiles; dy <= reachTiles; dy++)
        {
            for (int dx = -reachTiles; dx <= reachTiles; dx++)
            {
                if (_buildings.TryGetStructure(ptx + dx, pty + dy, out StructureTile st) &&
                    (st.Type == first || st.Type == second) &&
                    !st.IsDestroyed)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

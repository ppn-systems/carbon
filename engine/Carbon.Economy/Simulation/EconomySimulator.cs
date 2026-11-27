// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Carbon.Contracts.Common;
using Carbon.Economy.Config;
using Carbon.Economy.Currency;
using Carbon.Economy.Loot;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Repair;
using Carbon.Primitives.Random;

namespace Carbon.Economy.Simulation;

/// <summary>
/// Deterministic headless balance simulation: plays an engaged player (<see cref="PlayerModel"/>) for N hours at a level
/// against the real loot tables, vendor prices, repair costs and station fees, and reports faucets versus sinks.
/// Every gold movement goes through a real <see cref="Wallet"/>, so the report is the ledger, not a formula.
/// </summary>
public sealed class EconomySimulator
{
    private const long StartingGold = 1_000_000_000L;
    private readonly GameEconomy _economy;

    /// <summary>
    /// Initializes a new instance of the <see cref="EconomySimulator"/> class.
    /// </summary>
    /// <param name="economy">Loaded economy.</param>
    public EconomySimulator(GameEconomy economy)
    {
        ArgumentNullException.ThrowIfNull(economy);
        _economy = economy;
    }

    /// <summary>
    /// Simulates <paramref name="hours"/> of play at <paramref name="level"/>.
    /// </summary>
    /// <param name="level">Monster and player level.</param>
    /// <param name="hours">Whole hours to simulate.</param>
    /// <param name="key">Roll key of the simulated kills (any fixed key gives a reproducible report).</param>
    /// <returns>Average per-hour flows.</returns>
    /// <exception cref="InvalidDataException">Thrown when no monster table or player stage covers <paramref name="level"/>.</exception>
    public EconomyHourReport Simulate(int level, int hours, in GameplayKey key)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hours);
        EconomyConfig config = _economy.Config;
        PlayerModel model = config.PlayerModel;
        List<LootTable> tables = this.TablesFor(level);
        PlayerStage stage = StageFor(model, level);

        ReasonTotals ledger = new();
        Wallet wallet = new(1, long.MaxValue, long.MaxValue, ledger);
        wallet.Restore(StartingGold, 0, 0);
        InventoryContainer gear = this.EquipWeapon(stage);
        InventoryContainer bag = new(PlayerInventoryLayout.BagLimit);
        LootPityState pity = default;
        Span<LootDrop> drops = stackalloc LootDrop[32];
        LootContext context = new(level, PlayerLevel: level);
        (uint Id, int PerHour)[] consumables = this.ResolveConsumables(stage);
        int killsPerHour = config.DesignTargets.KillsPerHour;
        int recipeCursor = 0;

        for (int hour = 0; hour < hours; hour++)
        {
            for (int k = 0; k < killsPerHour; k++)
            {
                ulong killIndex = ((ulong)hour * (ulong)killsPerHour) + (ulong)k;
                LootTable table = tables[(int)(killIndex % (ulong)tables.Count)];
                DeterministicRandom random = LootRoller.CreateRandom(key, killIndex, (ulong)level);
                LootRollResult roll = LootRoller.Roll(table, context, ref random, ref pity, drops);

                // In the world, kill gold and diamonds drop as ground piles reserved for the killer for
                // groundLoot.ownerProtectionSeconds (ADR-019). The engaged player modelled here picks up every drop
                // inside that window, as it does the item stacks it sells below, so crediting at the kill gives the
                // same gold and diamonds per hour. Unpicked loot is a player choice, not a faucet change.
                _ = wallet.TryCredit(CurrencyKind.Gold, roll.Gold, CurrencyReason.MonsterLoot, killIndex);
                _ = wallet.TryCredit(CurrencyKind.Diamond, roll.Diamonds, CurrencyReason.MonsterLoot, killIndex);
                for (int d = 0; d < roll.ItemCount; d++)
                {
                    long sold = (long)drops[d].Quantity * model.SellLootPercent / 100;
                    _ = wallet.TryCredit(CurrencyKind.Gold, sold * _economy.Prices.GetSellPrice(drops[d].ItemId), CurrencyReason.VendorSale, killIndex);
                }

                this.WearAndRepair(wallet, gear, model, stage.StrikesPerKill, killIndex);
            }

            for (int c = 0; c < model.CraftsPerHour && stage.Recipes.Count > 0; c++)
            {
                if (_economy.Recipes.TryGetByCode(stage.Recipes[recipeCursor++ % stage.Recipes.Count], out CraftingRecipe? recipe))
                {
                    _ = wallet.TrySpend(CurrencyKind.Gold, _economy.Prices.GetCraftingFee(recipe), CurrencyReason.CraftingFee);
                }
            }

            foreach ((uint id, int perHour) in consumables)
            {
                if (perHour > 0 && !_economy.Vendor.TryBuy(wallet, bag, id, perHour, buyerLevel: level).IsOk)
                {
                    throw new InvalidDataException($"playerModel consumable {id} could not be bought at level {level}.");
                }

                bag.Clear(); // consumed during the hour
            }
        }

        double h = hours;
        return new EconomyHourReport(
            level,
            ledger.Gold(CurrencyReason.MonsterLoot) / h,
            ledger.Gold(CurrencyReason.VendorSale) / h,
            -ledger.Gold(CurrencyReason.Repair) / h,
            -ledger.Gold(CurrencyReason.CraftingFee) / h,
            -ledger.Gold(CurrencyReason.VendorPurchase) / h,
            wallet.Diamonds / h);
    }

    /// <summary>
    /// Gets the least-squares slope of net gold/hour against level (gold/hour gained per level): the inflation slope.
    /// </summary>
    /// <param name="reports">Reports at several levels.</param>
    /// <returns>Slope in gold/hour per level.</returns>
    public static double InflationSlope(IReadOnlyList<EconomyHourReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count < 2)
        {
            return 0d;
        }

        double meanX = 0d;
        double meanY = 0d;
        foreach (EconomyHourReport r in reports)
        {
            meanX += r.Level;
            meanY += r.Net;
        }

        meanX /= reports.Count;
        meanY /= reports.Count;
        double num = 0d;
        double den = 0d;
        foreach (EconomyHourReport r in reports)
        {
            num += (r.Level - meanX) * (r.Net - meanY);
            den += (r.Level - meanX) * (r.Level - meanX);
        }

        return den == 0d ? 0d : num / den;
    }

    /// <summary>
    /// Formats reports as a Markdown table (used by the balance test output and <c>docs/economy.md</c>).
    /// </summary>
    /// <param name="reports">Reports.</param>
    /// <returns>Markdown table text.</returns>
    public static string FormatTable(IReadOnlyList<EconomyHourReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        StringBuilder sb = new();
        _ = sb.AppendLine("| Level | Loot gold | Vendor sales | Repairs | Station fees | Vendor buys | **Net/h** | Sink share | Diamonds/h |");
        _ = sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (EconomyHourReport r in reports)
        {
            _ = sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {r.Level} | {r.MonsterGold:N0} | {r.VendorSales:N0} | {r.Repairs:N0} | {r.CraftingFees:N0} | {r.VendorPurchases:N0} | **{r.Net:N0}** | {r.SinkShare:P0} | {r.Diamonds:0.000} |"));
        }

        _ = sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"\nInflation slope: {InflationSlope(reports):N1} net gold/hour per level."));
        return sb.ToString();
    }

    private static PlayerStage StageFor(PlayerModel model, int level)
    {
        foreach (PlayerStage stage in model.Stages)
        {
            if (stage.MinLevel <= level && level <= stage.MaxLevel)
            {
                return stage;
            }
        }

        throw new InvalidDataException($"economy.json playerModel has no stage covering level {level}.");
    }

    private List<LootTable> TablesFor(int level)
    {
        List<LootTable> tables = [];
        foreach (LootTable table in _economy.Loot.All)
        {
            if (table.Kind == LootSourceKind.Monster && !table.IsElite && !table.IsBoss && table.MinLevel <= level && level <= table.MaxLevel)
            {
                tables.Add(table);
            }
        }

        return tables.Count > 0 ? tables : throw new InvalidDataException($"No normal monster loot table covers level {level}.");
    }

    private InventoryContainer EquipWeapon(PlayerStage stage)
    {
        ItemCatalogEntry weapon = _economy.Items.TryGetByCode(stage.Weapon, out ItemCatalogEntry? found)
            ? found
            : throw new InvalidDataException($"playerModel weapon '{stage.Weapon}' is not in items.json.");
        if (weapon.MaxDurability <= 0f)
        {
            throw new InvalidDataException($"playerModel weapon '{weapon.Code}' does not wear out.");
        }

        InventoryContainer gear = new(1);
        gear.SetSlot(0, new ItemSlot(weapon.Id, 1, InstanceId: 1, Durability: weapon.MaxDurability));
        return gear;
    }

    private (uint Id, int PerHour)[] ResolveConsumables(PlayerStage stage)
    {
        (uint Id, int PerHour)[] resolved = new (uint, int)[stage.Consumables.Count];
        for (int i = 0; i < resolved.Length; i++)
        {
            ConsumableUse use = stage.Consumables[i];
            resolved[i] = _economy.Items.TryGetByCode(use.Item, out ItemCatalogEntry? item)
                ? (item.Id, use.PerHour)
                : throw new InvalidDataException($"playerModel consumable '{use.Item}' is not in items.json.");
        }

        return resolved;
    }

    private void WearAndRepair(Wallet wallet, InventoryContainer gear, PlayerModel model, int strikesPerKill, ulong tick)
    {
        ItemSlot slot = gear.GetSlot(0);
        if (!_economy.Items.TryGet(slot.ItemId, out ItemCatalogEntry? item))
        {
            return;
        }

        // The same per-strike wear the server applies (ToolUsagePolicy), so the sink follows the real combat rules.
        float loss = ToolUsagePolicy.EvaluateCombatStrike(_economy.ItemRules, in slot).DurabilityLoss * strikesPerKill;
        ItemSlot worn = slot with { Durability = MathF.Max(0f, slot.Durability - loss) };
        gear.SetSlot(0, worn);
        // The blacksmith's gold fee for a full repair (repair.json, ADR-028); materials are gathered, not bought.
        if (worn.Durability * 100f < item.MaxDurability * model.RepairBelowPercent &&
            _economy.ItemRules.Repair.TryQuoteFull(in worn, RepairVenue.Blacksmith, out RepairQuote quote) == RepairError.None &&
            wallet.TrySpend(CurrencyKind.Gold, quote.GoldFee, CurrencyReason.Repair, tick).IsOk)
        {
            gear.SetSlot(0, worn with { Durability = quote.NewDurability });
        }
    }

    /// <summary>Sums ledger deltas per reason.</summary>
    private sealed class ReasonTotals : ICurrencyLedger
    {
        private readonly long[] _gold = new long[256];

        public void Record(in CurrencyLedgerEntry entry)
        {
            if (entry.Currency == CurrencyKind.Gold)
            {
                _gold[(byte)entry.Reason] += entry.Delta;
            }
        }

        public long Gold(CurrencyReason reason) => _gold[(byte)reason];
    }
}

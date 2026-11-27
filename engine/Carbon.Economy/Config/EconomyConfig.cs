// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Carbon.Economy.Serialization;

namespace Carbon.Economy.Config;

/// <summary>
/// Root of <c>resources/config/economy.json</c>: every economy tunable in one place.
/// </summary>
public sealed record EconomyConfig
{
    /// <summary>Gets the schema version.</summary>
    public int Version { get; set; }

    /// <summary>Gets the highest monster level the curves are defined for.</summary>
    public int MaxLevel { get; set; } = 100;

    /// <summary>Gets the wallet caps.</summary>
    public CurrencyRules Currencies { get; set; } = new();

    /// <summary>Gets the gold faucet rules.</summary>
    public GoldRules Gold { get; set; } = new();

    /// <summary>Gets the diamond faucet rules.</summary>
    public DiamondRules Diamond { get; set; } = new();

    /// <summary>Gets the vendor price spread.</summary>
    public VendorRules Vendor { get; set; } = new();

    /// <summary>Gets the repair sink.</summary>
    public RepairRules Repair { get; set; } = new();

    /// <summary>Gets the crafting fee sink.</summary>
    public CraftingFeeRules CraftingFees { get; set; } = new();

    /// <summary>Gets the owner protection and lifetime of monster kill loot on the ground.</summary>
    public GroundLootRules GroundLoot { get; set; } = new();

    /// <summary>Gets the gold a player drops on death (ADR-031).</summary>
    public DeathPenaltyRules DeathPenalty { get; set; } = new();

    /// <summary>Gets the designed income bands asserted by tests.</summary>
    public DesignTargets DesignTargets { get; set; } = new();

    /// <summary>Gets the engaged-player behaviour used by the balance simulation.</summary>
    public PlayerModel PlayerModel { get; set; } = new();

    /// <summary>
    /// Parses and validates an <c>economy.json</c> document.
    /// </summary>
    /// <param name="json">JSON document text.</param>
    /// <returns>The validated config.</returns>
    /// <exception cref="InvalidDataException">Thrown when the document is malformed or a tunable is out of range.</exception>
    public static EconomyConfig LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        EconomyConfig? config;
        try
        {
            config = JsonSerializer.Deserialize(json, EconomyJsonContext.Default.EconomyConfig);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"economy.json is malformed: {ex.Message}", ex);
        }

        if (config is null)
        {
            throw new InvalidDataException("economy.json is empty.");
        }

        config.Validate();
        return config;
    }

    /// <summary>
    /// Validates every tunable range and the anti-arbitrage invariant (sell price below buy price).
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown when a tunable is out of range.</exception>
    public void Validate()
    {
        Require(this.MaxLevel is >= 1 and <= 1000, "maxLevel must be in [1, 1000]");
        Require(this.Currencies.GoldCap > 0 && this.Currencies.DiamondCap > 0, "currency caps must be positive");
        Require(this.Gold.DropChancePercent is >= 0 and <= 100, "gold.dropChancePercent must be in [0, 100]");
        Require(this.Gold.LevelExponent is >= 0d and <= 3d, "gold.levelExponent must be in [0, 3]");
        Require(this.Gold.VariancePercent is >= 0 and <= 100, "gold.variancePercent must be in [0, 100]");
        Require(this.Gold.EliteMultiplier >= 1 && this.Gold.BossMultiplier >= 1, "gold multipliers must be >= 1");
        Require(this.Gold.DistanceBonusPercentPerTier >= 0 && this.Gold.MaxDistanceTier >= 0, "gold distance bonus must be >= 0");
        Require(this.Gold.LowLevelGap >= 0, "gold.lowLevelGap must be >= 0");
        Require(this.Gold.LowLevelMultiplierPercent is >= 0 and <= 100, "gold.lowLevelMultiplierPercent must be in [0, 100]");
        Require(this.Diamond.LevelBonusPercentPerLevel >= 0, "diamond.levelBonusPercentPerLevel must be >= 0");
        Require(this.Diamond.EliteMultiplier >= 1 && this.Diamond.BossMultiplier >= 1, "diamond multipliers must be >= 1");
        Require(this.Diamond.DistanceBonusPercentPerTier >= 0, "diamond.distanceBonusPercentPerTier must be >= 0");
        Require(this.Diamond.MaxChancePpm is > 0 and <= 1_000_000, "diamond.maxChancePpm must be in (0, 1000000]");
        Require(
            !this.Diamond.Pity.Enabled
            || (this.Diamond.Pity.SoftStartKills > 0 && this.Diamond.Pity.HardCapKills > this.Diamond.Pity.SoftStartKills && this.Diamond.Pity.StepPpm >= 0),
            "diamond.pity requires 0 < softStartKills < hardCapKills and stepPpm >= 0");
        Require(this.Vendor.BuyMarkupPercent >= 1, "vendor.buyMarkupPercent must be >= 1");
        Require(
            this.Vendor.SellRatioPercent >= 0 && this.Vendor.SellRatioPercent < this.Vendor.BuyMarkupPercent,
            "vendor.sellRatioPercent must be >= 0 and below buyMarkupPercent (otherwise buy-then-sell mints gold)");
        Require(this.Repair.CostPercentOfBasePrice >= 0 && this.Repair.MinimumCost >= 0, "repair costs must be >= 0");
        Require(this.Repair.RarityMultiplierPercent.IsValid, "repair.rarityMultiplierPercent values must be >= 0");
        Require(
            this.CraftingFees.None >= 0 && this.CraftingFees.Workbench >= 0 && this.CraftingFees.Forge >= 0
            && this.CraftingFees.Stove >= 0 && this.CraftingFees.Alchemy >= 0 && this.CraftingFees.OutputValueRateBps is >= 0 and <= 10000,
            "crafting fees must be >= 0 and outputValueRateBps in [0, 10000]");
        Require(this.CraftingFees.RarityMultiplierPercent.IsValid, "craftingFees.rarityMultiplierPercent values must be >= 0");
        Require(
            this.GroundLoot.KillLootLifetimeSeconds is >= 1 and <= 3600
            && this.GroundLoot.OwnerProtectionSeconds >= 0
            && this.GroundLoot.OwnerProtectionSeconds <= this.GroundLoot.KillLootLifetimeSeconds,
            "groundLoot needs 1 <= killLootLifetimeSeconds <= 3600 and 0 <= ownerProtectionSeconds <= killLootLifetimeSeconds");
        Require(
            this.DeathPenalty.MinLevel >= 1 && this.DeathPenalty.DropPercent is >= 0 and <= 100
            && this.DeathPenalty.FloorGoldPerLevel >= 0 && this.DeathPenalty.CapGoldPerLevel >= this.DeathPenalty.FloorGoldPerLevel,
            "deathPenalty needs minLevel >= 1, dropPercent in [0, 100] and 0 <= floorGoldPerLevel <= capGoldPerLevel");
        Require(
            this.DeathPenalty.GoldPerPile >= 1 && this.DeathPenalty.MaxPiles is >= 1 and <= Death.DeathGoldPenalty.MaxPilesLimit,
            "deathPenalty needs goldPerPile >= 1 and maxPiles in [1, 16]");
        Require(
            this.DeathPenalty.LootLifetimeSeconds is >= 1 and <= 3600
            && this.DeathPenalty.OwnerProtectionSeconds >= 0
            && this.DeathPenalty.OwnerProtectionSeconds <= this.DeathPenalty.LootLifetimeSeconds,
            "deathPenalty needs 1 <= lootLifetimeSeconds <= 3600 and 0 <= ownerProtectionSeconds <= lootLifetimeSeconds");
        this.ValidateConsumablePriceBands();
        Require(this.DesignTargets.KillsPerHour > 0, "designTargets.killsPerHour must be positive");
        Require(this.DesignTargets.InflationSlope.Min <= this.DesignTargets.InflationSlope.Max, "designTargets.inflationSlope min must be <= max");
        Require(
            this.PlayerModel.SellLootPercent is >= 0 and <= 100 && this.PlayerModel.RepairBelowPercent is >= 0 and <= 100
            && this.PlayerModel.CraftsPerHour >= 0,
            "playerModel percents must be in [0, 100] and craftsPerHour >= 0");
        foreach (PlayerStage stage in this.PlayerModel.Stages)
        {
            Require(stage.MinLevel >= 1 && stage.MaxLevel >= stage.MinLevel && stage.StrikesPerKill >= 1, "playerModel.stages need 1 <= minLevel <= maxLevel and strikesPerKill >= 1");
            foreach (ConsumableUse use in stage.Consumables)
            {
                Require(use.Item.Length > 0 && use.PerHour >= 0, "playerModel consumables need an item and perHour >= 0");
            }
        }
    }

    /// <summary>Returns the consumable price percentage for a buyer at <paramref name="level"/> (100 outside every band).</summary>
    /// <param name="level">Buyer level (0 = unknown).</param>
    /// <returns>The price percentage.</returns>
    public int GetConsumablePricePercent(int level)
    {
        foreach (PriceBand band in this.Vendor.ConsumablePriceBands)
        {
            if (band.MinLevel <= level && level <= band.MaxLevel)
            {
                return band.Percent;
            }
        }

        return 100;
    }

    private void ValidateConsumablePriceBands()
    {
        IReadOnlyList<PriceBand> bands = this.Vendor.ConsumablePriceBands;
        for (int i = 0; i < bands.Count; i++)
        {
            // Percent >= 100 keeps the anti-arbitrage invariant: the sell price (base x sellRatio) stays below any buy price.
            Require(
                bands[i].MinLevel >= 1 && bands[i].MaxLevel >= bands[i].MinLevel && bands[i].Percent >= 100,
                "vendor.consumablePriceBands need 1 <= minLevel <= maxLevel and percent >= 100");
            for (int j = 0; j < i; j++)
            {
                Require(
                    bands[i].MaxLevel < bands[j].MinLevel || bands[i].MinLevel > bands[j].MaxLevel,
                    "vendor.consumablePriceBands must not overlap");
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException($"economy.json: {message}.");
        }
    }
}

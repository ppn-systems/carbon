// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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

    /// <summary>Gets the designed income bands asserted by tests.</summary>
    public DesignTargets DesignTargets { get; set; } = new();

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
        Require(
            this.CraftingFees.None >= 0 && this.CraftingFees.Workbench >= 0 && this.CraftingFees.Forge >= 0
            && this.CraftingFees.Stove >= 0 && this.CraftingFees.Alchemy >= 0 && this.CraftingFees.OutputValueRateBps is >= 0 and <= 10000,
            "crafting fees must be >= 0 and outputValueRateBps in [0, 10000]");
        Require(this.DesignTargets.KillsPerHour > 0, "designTargets.killsPerHour must be positive");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException($"economy.json: {message}.");
        }
    }
}

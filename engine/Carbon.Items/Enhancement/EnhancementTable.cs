// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Serialization;
using Carbon.Primitives.Random;

namespace Carbon.Items.Enhancement;

/// <summary>
/// The validated forge enhancement table compiled from <c>enhancement.json</c> (ADR-020): per-level outcome weights,
/// materials and gold fee, the safe thresholds, and the protection scrolls. Immutable; the server and each synced client
/// hold one per <see cref="ItemRules"/>.
/// </summary>
public sealed class EnhancementTable
{
    /// <summary>The outcome weights of every row sum to this (per mille).</summary>
    public const int WeightTotal = 1000;

    private readonly EnhancementStep[] _steps;
    private readonly EnhancementProtection[] _protections;
    private readonly EnhancementRarityPercents _rarity;

    private EnhancementTable(
        EnhancementStep[] steps,
        EnhancementProtection[] protections,
        EnhancementRarityPercents rarity,
        byte downgradeMinLevel,
        byte destroyMinLevel,
        byte announceSuccessMinLevel,
        bool announceDestroyed)
    {
        _steps = steps;
        _protections = protections;
        _rarity = rarity;
        this.DowngradeMinLevel = downgradeMinLevel;
        this.DestroyMinLevel = destroyMinLevel;
        this.AnnounceSuccessMinLevel = announceSuccessMinLevel;
        this.AnnounceDestroyed = announceDestroyed;
    }

    /// <summary>Gets the highest level the forge can reach.</summary>
    public byte MaxLevel => (byte)_steps.Length;

    /// <summary>Gets the lowest current level from which a failure may drop a level.</summary>
    public byte DowngradeMinLevel { get; }

    /// <summary>Gets the lowest current level from which a failure may destroy the item.</summary>
    public byte DestroyMinLevel { get; }

    /// <summary>Gets the lowest level reached by a success that is announced world-wide (<c>0</c>: never).</summary>
    public byte AnnounceSuccessMinLevel { get; }

    /// <summary>Gets whether a destroyed item is announced world-wide.</summary>
    public bool AnnounceDestroyed { get; }

    /// <summary>Returns whether an attempt with <paramref name="outcome"/> that left the item at <paramref name="newLevel"/> is announced world-wide.</summary>
    /// <param name="outcome">Rolled outcome.</param>
    /// <param name="newLevel">Level after the attempt.</param>
    /// <returns><see langword="true"/> for a success at or above <see cref="AnnounceSuccessMinLevel"/>, or a destroyed item when <see cref="AnnounceDestroyed"/>.</returns>
    public bool IsAnnounced(EnhancementOutcome outcome, byte newLevel) =>
        (outcome == EnhancementOutcome.Success && this.AnnounceSuccessMinLevel > 0 && newLevel >= this.AnnounceSuccessMinLevel)
        || (outcome == EnhancementOutcome.Destroyed && this.AnnounceDestroyed);

    /// <summary>Gets every attempt row, indexed by current level.</summary>
    public ReadOnlySpan<EnhancementStep> Steps => _steps;

    /// <summary>Gets every protection scroll.</summary>
    public ReadOnlySpan<EnhancementProtection> Protections => _protections;

    /// <summary>Parses, validates and compiles an <c>enhancement.json</c> document.</summary>
    /// <param name="json">Document text.</param>
    /// <param name="items">Item catalog used to resolve material and scroll codes.</param>
    /// <returns>The compiled table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the document is malformed or inconsistent.</exception>
    public static EnhancementTable LoadFromJson(string json, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(items);
        EnhancementDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, ItemsJsonContext.Default.EnhancementDocument);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"enhancement.json is malformed: {ex.Message}", ex);
        }

        return Compile(document ?? throw new InvalidDataException("enhancement.json is empty."), items);
    }

    /// <summary>Validates and compiles <paramref name="document"/>.</summary>
    /// <param name="document">Parsed document.</param>
    /// <param name="items">Item catalog used to resolve codes.</param>
    /// <returns>The compiled table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the document is inconsistent.</exception>
    public static EnhancementTable Compile(EnhancementDocument document, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(items);

        if (document.MaxLevel < 1 || document.MaxLevel > ItemEnhancementPolicy.MaxUpgradeLevel)
        {
            throw new InvalidDataException($"enhancement.json maxLevel must be 1..{ItemEnhancementPolicy.MaxUpgradeLevel}.");
        }

        if (document.DowngradeMinLevel < 1 || document.DestroyMinLevel < document.DowngradeMinLevel)
        {
            throw new InvalidDataException("enhancement.json needs 1 <= downgradeMinLevel <= destroyMinLevel.");
        }

        EnhancementRarityPercents rarity = document.GoldRarityPercent ?? new EnhancementRarityPercents();
        if (rarity.Common < 0 || rarity.Uncommon < 0 || rarity.Rare < 0 || rarity.Epic < 0 || rarity.Legendary < 0)
        {
            throw new InvalidDataException("enhancement.json goldRarityPercent values must not be negative.");
        }

        EnhancementAnnounceRules announce = document.Announce ?? new EnhancementAnnounceRules();
        if (announce.SuccessMinLevel < 0 || announce.SuccessMinLevel > document.MaxLevel)
        {
            throw new InvalidDataException("enhancement.json announce.successMinLevel must be 0..maxLevel.");
        }

        IReadOnlyList<EnhancementLevelDefinition> levels = document.Levels ?? [];
        if (levels.Count != document.MaxLevel)
        {
            throw new InvalidDataException($"enhancement.json needs exactly one levels[] row per level 0..{document.MaxLevel - 1}.");
        }

        EnhancementStep[] steps = new EnhancementStep[levels.Count];
        for (int i = 0; i < levels.Count; i++)
        {
            steps[i] = CompileLevel(levels[i], i, document, items);
        }

        IReadOnlyList<EnhancementProtectionDefinition> protectionDefs = document.Protections ?? [];
        EnhancementProtection[] protections = new EnhancementProtection[protectionDefs.Count];
        for (int i = 0; i < protections.Length; i++)
        {
            EnhancementProtectionDefinition def = protectionDefs[i];
            if (!items.TryGetByCode(def.Item, out ItemCatalogEntry? item))
            {
                throw new InvalidDataException($"enhancement.json protection '{def.Item}' is not in items.json.");
            }

            if (!def.PreventsDowngrade && !def.PreventsDestroy)
            {
                throw new InvalidDataException($"enhancement.json protection '{def.Item}' prevents nothing.");
            }

            for (int j = 0; j < i; j++)
            {
                if (protections[j].ItemId == item.Id)
                {
                    throw new InvalidDataException($"enhancement.json lists protection '{def.Item}' twice.");
                }
            }

            protections[i] = new EnhancementProtection(item.Id, def.PreventsDowngrade, def.PreventsDestroy);
        }

        return new EnhancementTable(
            steps,
            protections,
            rarity,
            (byte)document.DowngradeMinLevel,
            (byte)document.DestroyMinLevel,
            (byte)announce.SuccessMinLevel,
            announce.Destroyed);
    }

    /// <summary>Gets the attempt row for an item currently at <paramref name="currentLevel"/>.</summary>
    /// <param name="currentLevel">Current item level.</param>
    /// <param name="step">The row when the item can still be enhanced.</param>
    /// <returns><see langword="false"/> at or above <see cref="MaxLevel"/>.</returns>
    public bool TryGetStep(byte currentLevel, [NotNullWhen(true)] out EnhancementStep? step)
    {
        if (currentLevel < _steps.Length)
        {
            step = _steps[currentLevel];
            return true;
        }

        step = null;
        return false;
    }

    /// <summary>Looks up a protection scroll by item id.</summary>
    /// <param name="itemId">Scroll item id.</param>
    /// <param name="protection">The scroll when known.</param>
    /// <returns><see langword="true"/> when <paramref name="itemId"/> is a protection scroll.</returns>
    public bool TryGetProtection(uint itemId, out EnhancementProtection protection)
    {
        for (int i = 0; i < _protections.Length; i++)
        {
            if (_protections[i].ItemId == itemId)
            {
                protection = _protections[i];
                return true;
            }
        }

        protection = default;
        return false;
    }

    /// <summary>Returns the gold fee of <paramref name="step"/> for an item of <paramref name="rarity"/>.</summary>
    /// <param name="step">Attempt row.</param>
    /// <param name="rarity">Item rarity.</param>
    /// <returns>The fee in gold.</returns>
    public long GetGoldFee(EnhancementStep step, ItemRarity rarity)
    {
        ArgumentNullException.ThrowIfNull(step);
        int percent = rarity switch
        {
            ItemRarity.Common => _rarity.Common,
            ItemRarity.Uncommon => _rarity.Uncommon,
            ItemRarity.Rare => _rarity.Rare,
            ItemRarity.Epic => _rarity.Epic,
            ItemRarity.Legendary => _rarity.Legendary,
            _ => 100,
        };
        return step.BaseGold * percent / 100;
    }

    /// <summary>
    /// Rolls one attempt of <paramref name="step"/> and applies <paramref name="protection"/> (when given) to a failure.
    /// </summary>
    /// <param name="step">Attempt row.</param>
    /// <param name="random">Server-side deterministic stream.</param>
    /// <param name="protection">The scroll spent on the attempt, if any.</param>
    /// <returns>The outcome.</returns>
    public static EnhancementOutcome Roll(EnhancementStep step, ref DeterministicRandom random, EnhancementProtection? protection = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        return Resolve(step, random.NextInt(0, WeightTotal), protection);
    }

    /// <summary>Maps a roll in <c>[0, 1000)</c> to the outcome of <paramref name="step"/>, then applies <paramref name="protection"/>.</summary>
    /// <param name="step">Attempt row.</param>
    /// <param name="roll">Roll in <c>[0, <see cref="WeightTotal"/>)</c>.</param>
    /// <param name="protection">The scroll spent on the attempt, if any.</param>
    /// <returns>The outcome.</returns>
    public static EnhancementOutcome Resolve(EnhancementStep step, int roll, EnhancementProtection? protection = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        EnhancementOutcome raw;
        if (roll < step.SuccessPermille)
        {
            raw = EnhancementOutcome.Success;
        }
        else if (roll < step.SuccessPermille + step.NoChangePermille)
        {
            raw = EnhancementOutcome.NoChange;
        }
        else if (roll < step.SuccessPermille + step.NoChangePermille + step.DowngradePermille)
        {
            raw = EnhancementOutcome.Downgrade;
        }
        else
        {
            raw = EnhancementOutcome.Destroyed;
        }

        if (protection is not { } guard)
        {
            return raw;
        }

        if (raw == EnhancementOutcome.Destroyed && guard.PreventsDestroy)
        {
            raw = EnhancementOutcome.Downgrade;
        }

        return raw == EnhancementOutcome.Downgrade && guard.PreventsDowngrade ? EnhancementOutcome.NoChange : raw;
    }

    private static EnhancementStep CompileLevel(EnhancementLevelDefinition row, int index, EnhancementDocument document, ItemCatalog items)
    {
        if (row.From != index)
        {
            throw new InvalidDataException($"enhancement.json levels[{index}] must have from = {index}.");
        }

        if (row.Success <= 0 || row.NoChange < 0 || row.Downgrade < 0 || row.Destroy < 0 ||
            row.Success + row.NoChange + row.Downgrade + row.Destroy != WeightTotal)
        {
            throw new InvalidDataException($"enhancement.json level {index}: weights must be non-negative, success > 0, and sum to {WeightTotal}.");
        }

        if (row.Downgrade > 0 && index < document.DowngradeMinLevel)
        {
            throw new InvalidDataException($"enhancement.json level {index} is below downgradeMinLevel and must not downgrade.");
        }

        if (row.Destroy > 0 && index < document.DestroyMinLevel)
        {
            throw new InvalidDataException($"enhancement.json level {index} is below destroyMinLevel and must not destroy.");
        }

        if (row.Gold < 0)
        {
            throw new InvalidDataException($"enhancement.json level {index}: gold must not be negative.");
        }

        IReadOnlyList<RecipeItemAmount> mats = row.Materials ?? [];
        RecipeIngredient[] materials = new RecipeIngredient[mats.Count];
        for (int k = 0; k < materials.Length; k++)
        {
            RecipeItemAmount amount = mats[k];
            if (!items.TryGetByCode(amount.Item, out ItemCatalogEntry? item) || amount.Qty <= 0)
            {
                throw new InvalidDataException($"enhancement.json level {index}: material '{amount.Item}' is unknown or has qty <= 0.");
            }

            for (int j = 0; j < k; j++)
            {
                if (materials[j].ItemId == item.Id)
                {
                    throw new InvalidDataException($"enhancement.json level {index} lists material '{amount.Item}' twice.");
                }
            }

            materials[k] = new RecipeIngredient(item.Id, amount.Qty);
        }

        return new EnhancementStep((byte)index, row.Success, row.NoChange, row.Downgrade, row.Destroy, row.Gold, materials);
    }
}

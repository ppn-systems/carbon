// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Carbon.Items.Definitions;
using Carbon.Survival.Serialization;

namespace Carbon.Survival.Stamina;

/// <summary>
/// Stamina tuning (ADR-033), loaded from <c>resources/config/stamina.json</c>. The server runs it; clients get the same
/// document through the config sync (ADR-014) and only use it to predict and display.
/// </summary>
public sealed class StaminaConfig
{
    /// <summary>Config document name (<c>stamina.json</c>).</summary>
    public const string DocumentName = "stamina";

    /// <summary>Gets the stamina drained per second of sprinting.</summary>
    public float SprintDrainPerSecond { get; init; }

    /// <summary>Gets the seconds after the last spend before regen starts.</summary>
    public float RegenDelaySeconds { get; init; }

    /// <summary>Gets the stamina regenerated per second while standing still.</summary>
    public float RegenPerSecondStill { get; init; }

    /// <summary>Gets the stamina regenerated per second while moving (walking).</summary>
    public float RegenPerSecondMoving { get; init; }

    /// <summary>Gets the regen multiplier while sitting or sleeping.</summary>
    public float RestingRegenMultiplier { get; init; } = 1f;

    /// <summary>Gets the fraction of max stamina an exhausted player must regain before acting or sprinting again.</summary>
    public float ExhaustedRecoverFraction { get; init; }

    /// <summary>Gets the floor of the combined fatigue, hunger and effect multiplier, so regen never stops.</summary>
    public float MinimumRegenScale { get; init; }

    /// <summary>Gets the regen multipliers per rest band.</summary>
    public StaminaFatigueScales FatigueRegenScale { get; init; } = new();

    /// <summary>Gets the regen multipliers per hunger band.</summary>
    public StaminaHungerScales HungerRegenScale { get; init; } = new();

    /// <summary>Gets the default swing cost per swing kind.</summary>
    public StaminaSwingCosts SwingCosts { get; init; } = new();

    /// <summary>Gets per-item swing costs by item code; they override <see cref="SwingCosts"/>.</summary>
    public Dictionary<string, float> ItemSwingCosts { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Gets the stamina drained per second while a construction or deconstruction job makes progress.</summary>
    public float BuildWorkPerSecond { get; init; }

    /// <summary>Parses and validates <c>stamina.json</c>.</summary>
    /// <param name="json">JSON object text.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="InvalidDataException">Thrown when the payload is empty, malformed or a value is out of range.</exception>
    public static StaminaConfig LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        StaminaConfig config;
        try
        {
            config = JsonSerializer.Deserialize(json, SurvivalJsonContext.Default.StaminaConfig)
                ?? throw new InvalidDataException("stamina.json is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"stamina.json is malformed: {ex.Message}", ex);
        }

        config.Validate();
        return config;
    }

    /// <summary>Loads the copy of <c>stamina.json</c> embedded at build time (the server's default).</summary>
    /// <returns>The shipped configuration.</returns>
    public static StaminaConfig LoadShippedDefaults()
    {
        using Stream stream = typeof(StaminaConfig).Assembly.GetManifestResourceStream("Carbon.Survival.Config.stamina.json")
            ?? throw new InvalidOperationException("Embedded default config 'stamina.json' is missing.");
        using StreamReader reader = new(stream);
        return LoadFromJson(reader.ReadToEnd());
    }

    /// <summary>Classifies what <paramref name="item"/> swings as (<see langword="null"/>: empty hand).</summary>
    /// <param name="item">Held item, if any.</param>
    /// <returns>The swing kind.</returns>
    public static StaminaSwingKind ClassifySwing(ItemArchetype? item)
    {
        if (item is null)
        {
            return StaminaSwingKind.BareHand;
        }

        if (item.Capabilities.HasFlag(ToolCapabilities.MagicFocus))
        {
            return StaminaSwingKind.Spell;
        }

        if (item.Capabilities.HasFlag(ToolCapabilities.RangedBow))
        {
            return StaminaSwingKind.Ranged;
        }

        if (item.Category == ItemCategory.Tool)
        {
            return StaminaSwingKind.Tool;
        }

        return item.Category == ItemCategory.Weapon || item.Capabilities.HasFlag(ToolCapabilities.MeleeWeapon)
            ? StaminaSwingKind.Melee
            : StaminaSwingKind.BareHand;
    }

    /// <summary>
    /// Gets the stamina cost of one primary swing with <paramref name="item"/>: its <see cref="ItemSwingCosts"/> entry,
    /// else the default for its <see cref="StaminaSwingKind"/>.
    /// </summary>
    /// <param name="item">Held item (<see langword="null"/>: empty hand).</param>
    /// <returns>The swing cost.</returns>
    public float GetSwingCost(ItemArchetype? item)
    {
        if (item is not null && this.ItemSwingCosts.TryGetValue(item.Code, out float cost))
        {
            return cost;
        }

        return this.SwingCosts.For(ClassifySwing(item));
    }

    /// <summary>Gets the regen rate per second for <paramref name="context"/>.</summary>
    /// <param name="context">What the body is doing.</param>
    /// <returns>Stamina per second.</returns>
    public float GetRegenPerSecond(in StaminaRegenContext context)
    {
        float rate = context.IsMoving ? this.RegenPerSecondMoving : this.RegenPerSecondStill;
        if (context.IsResting)
        {
            rate *= this.RestingRegenMultiplier;
        }

        float scale = this.FatigueRegenScale.For(context.Fatigue) * this.HungerRegenScale.For(context.Hunger) * context.EffectScale;
        return rate * MathF.Max(this.MinimumRegenScale, scale);
    }

    /// <summary>Checks every value against its allowed range.</summary>
    /// <exception cref="InvalidDataException">Thrown when a value is out of range.</exception>
    public void Validate()
    {
        RequireRange(this.SprintDrainPerSecond, 0.1f, 1_000f, "sprintDrainPerSecond");
        RequireRange(this.RegenDelaySeconds, 0f, 30f, "regenDelaySeconds");
        RequireRange(this.RegenPerSecondStill, 0.1f, 1_000f, "regenPerSecondStill");
        RequireRange(this.RegenPerSecondMoving, 0.1f, this.RegenPerSecondStill, "regenPerSecondMoving");
        RequireRange(this.RestingRegenMultiplier, 1f, 10f, "restingRegenMultiplier");
        RequireRange(this.ExhaustedRecoverFraction, 0.01f, 1f, "exhaustedRecoverFraction");
        RequireRange(this.MinimumRegenScale, 0.01f, 1f, "minimumRegenScale");
        RequireRange(this.BuildWorkPerSecond, 0f, 1_000f, "buildWorkPerSecond");
        if (this.FatigueRegenScale is null || this.HungerRegenScale is null || this.SwingCosts is null || this.ItemSwingCosts is null)
        {
            throw new InvalidDataException("stamina.json must define fatigueRegenScale, hungerRegenScale, swingCosts and itemSwingCosts.");
        }

        this.FatigueRegenScale.Validate();
        this.HungerRegenScale.Validate();
        this.SwingCosts.Validate();
        foreach (KeyValuePair<string, float> entry in this.ItemSwingCosts)
        {
            RequireRange(entry.Value, 0f, 1_000f, $"itemSwingCosts.{entry.Key}");
        }
    }

    internal static void RequireRange(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
        {
            throw new InvalidDataException($"stamina.json '{name}' = {value} is outside [{min}, {max}].");
        }
    }
}

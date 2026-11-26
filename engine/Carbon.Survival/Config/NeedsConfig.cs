// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text.Json;
using Carbon.Survival.Components;
using Carbon.Survival.Serialization;

namespace Carbon.Survival.Config;

/// <summary>
/// Tuning for the survival needs systems, loaded from <c>resources/config/needs.json</c>.
/// Rates are expressed per real minute; thresholds are fractions of the need's maximum.
/// </summary>
public sealed class NeedsConfig
{
    /// <summary>Gets the food capacity.</summary>
    public float FoodMax { get; init; } = 100f;

    /// <summary>Gets the food drained per minute at activity multiplier 1 (walking).</summary>
    public float FoodDecayPerMinute { get; init; } = 100f / 60f;

    /// <summary>Gets the per-activity multipliers for food drain.</summary>
    public ActivityRates FoodActivity { get; init; } = new()
    {
        Idle = 0.5f,
        Walking = 1f,
        Sprinting = 2f,
        Fighting = 1.75f,
        Swimming = 2.5f,
        Resting = 0.4f,
    };

    /// <summary>Gets the food fraction below which the entity is <see cref="HungerState.Hungry"/>.</summary>
    public float HungryThreshold { get; init; } = 0.25f;

    /// <summary>Gets the food fraction below which the entity is <see cref="HungerState.UrgentlyHungry"/>.</summary>
    public float UrgentlyHungryThreshold { get; init; } = 0.125f;

    /// <summary>Gets the food fraction at or above which eating is refused (overeating cap).</summary>
    public float FullThreshold { get; init; } = 0.9f;

    /// <summary>Gets the mood offset while hungry.</summary>
    public float HungryMood { get; init; } = -6f;

    /// <summary>Gets the mood offset while urgently hungry.</summary>
    public float UrgentlyHungryMood { get; init; } = -12f;

    /// <summary>Gets the mood offset while starving.</summary>
    public float StarvingMood { get; init; } = -20f;

    /// <summary>Gets the movement and attack speed multiplier while urgently hungry.</summary>
    public float UrgentlyHungrySpeedScale { get; init; } = 0.8f;

    /// <summary>Gets the movement and attack speed multiplier while starving.</summary>
    public float StarvingSpeedScale { get; init; } = 0.65f;

    /// <summary>Gets the malnutrition severity gained per minute while starving.</summary>
    public float MalnutritionGainPerMinute { get; init; } = 0.1f;

    /// <summary>Gets the malnutrition severity recovered per minute while not starving.</summary>
    public float MalnutritionRecoveryPerMinute { get; init; } = 0.05f;

    /// <summary>Gets the seconds between starvation damage pulses.</summary>
    public float StarvationDamageIntervalSeconds { get; init; } = 5f;

    /// <summary>Gets the flat damage of each starvation pulse.</summary>
    public float StarvationDamageBase { get; init; } = 0.25f;

    /// <summary>Gets the extra pulse damage per point of malnutrition severity (0..1).</summary>
    public float StarvationDamagePerSeverity { get; init; } = 1f;

    /// <summary>Gets a value indicating whether rest drains at all. Hosts without a rest action set this to false.</summary>
    public bool RestEnabled { get; init; } = true;

    /// <summary>Gets the rest capacity.</summary>
    public float RestMax { get; init; } = 100f;

    /// <summary>Gets the rest drained per minute at activity multiplier 1 while awake.</summary>
    public float RestDecayPerMinute { get; init; } = 100f / 120f;

    /// <summary>Gets the per-activity multipliers for rest drain (the resting entry is ignored).</summary>
    public ActivityRates RestActivity { get; init; } = new()
    {
        Idle = 0.8f,
        Walking = 1f,
        Sprinting = 1.5f,
        Fighting = 1.5f,
        Swimming = 1.5f,
        Resting = 0f,
    };

    /// <summary>Gets the rest recovered per minute while resting.</summary>
    public float RestRecoveryPerMinute { get; init; } = 10f;

    /// <summary>Gets the rest fraction below which the entity is <see cref="RestState.Tired"/>.</summary>
    public float TiredThreshold { get; init; } = 0.28f;

    /// <summary>Gets the rest fraction below which the entity is <see cref="RestState.VeryTired"/>.</summary>
    public float VeryTiredThreshold { get; init; } = 0.14f;

    /// <summary>Gets the mood offset while tired.</summary>
    public float TiredMood { get; init; } = -6f;

    /// <summary>Gets the mood offset while very tired.</summary>
    public float VeryTiredMood { get; init; } = -12f;

    /// <summary>Gets the mood offset while exhausted.</summary>
    public float ExhaustedMood { get; init; } = -18f;

    /// <summary>Gets the movement and attack speed multiplier while exhausted.</summary>
    public float ExhaustedSpeedScale { get; init; } = 0.85f;

    /// <summary>Gets the baseline mood with no thoughts.</summary>
    public float MoodBaseline { get; init; } = 50f;

    /// <summary>Gets the mood below which the entity is <see cref="MoodState.Miserable"/>.</summary>
    public float MiserableBelow { get; init; } = 20f;

    /// <summary>Gets the mood below which the entity is <see cref="MoodState.Stressed"/>.</summary>
    public float StressedBelow { get; init; } = 35f;

    /// <summary>Gets the mood at or above which the entity is <see cref="MoodState.Content"/>.</summary>
    public float ContentAtOrAbove { get; init; } = 65f;

    /// <summary>Gets the mood at or above which the entity is <see cref="MoodState.Happy"/>.</summary>
    public float HappyAtOrAbove { get; init; } = 85f;

    /// <summary>Gets the seconds food poisoning lasts.</summary>
    public float PoisonDurationSeconds { get; init; } = 120f;

    /// <summary>Gets the mood offset while poisoned.</summary>
    public float PoisonMood { get; init; } = -12f;

    /// <summary>Gets the movement and attack speed multiplier while poisoned.</summary>
    public float PoisonSpeedScale { get; init; } = 0.85f;

    /// <summary>
    /// Parses and validates a <c>needs.json</c> payload.
    /// </summary>
    /// <param name="json">JSON object text.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="InvalidDataException">Thrown when the payload is empty or a value is out of range.</exception>
    public static NeedsConfig LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        NeedsConfig config = JsonSerializer.Deserialize(json, SurvivalJsonContext.Default.NeedsConfig)
            ?? throw new InvalidDataException("needs.json is empty.");
        config.Validate();
        return config;
    }

    /// <summary>
    /// Classifies a mood value into a <see cref="MoodState"/> band.
    /// </summary>
    /// <param name="mood">Mood value, 0..100.</param>
    /// <returns>The mood band.</returns>
    public MoodState ClassifyMood(float mood) =>
        mood < this.MiserableBelow ? MoodState.Miserable
        : mood < this.StressedBelow ? MoodState.Stressed
        : mood >= this.HappyAtOrAbove ? MoodState.Happy
        : mood >= this.ContentAtOrAbove ? MoodState.Content
        : MoodState.Neutral;

    /// <summary>
    /// Classifies a food fraction into a <see cref="HungerState"/>.
    /// </summary>
    /// <param name="foodFraction">Food level as a 0..1 fraction.</param>
    /// <returns>The hunger state.</returns>
    public HungerState ClassifyHunger(float foodFraction) =>
        foodFraction <= 0f ? HungerState.Starving
        : foodFraction < this.UrgentlyHungryThreshold ? HungerState.UrgentlyHungry
        : foodFraction < this.HungryThreshold ? HungerState.Hungry
        : HungerState.Fed;

    /// <summary>
    /// Classifies a rest fraction into a <see cref="RestState"/>.
    /// </summary>
    /// <param name="restFraction">Rest level as a 0..1 fraction.</param>
    /// <returns>The rest state.</returns>
    public RestState ClassifyRest(float restFraction) =>
        restFraction <= 0f ? RestState.Exhausted
        : restFraction < this.VeryTiredThreshold ? RestState.VeryTired
        : restFraction < this.TiredThreshold ? RestState.Tired
        : RestState.Rested;

    /// <summary>
    /// Checks every value against its allowed range.
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown when a value is out of range or thresholds are misordered.</exception>
    public void Validate()
    {
        RequireRange(this.FoodMax, 1f, 10_000f, "foodMax");
        RequireRange(this.FoodDecayPerMinute, 0.001f, 1_000f, "foodDecayPerMinute");
        RequireRange(this.HungryThreshold, 0.01f, 1f, "hungryThreshold");
        RequireRange(this.UrgentlyHungryThreshold, 0.001f, this.HungryThreshold, "urgentlyHungryThreshold");
        RequireRange(this.FullThreshold, this.HungryThreshold, 1f, "fullThreshold");
        RequireRange(this.HungryMood, -100f, 0f, "hungryMood");
        RequireRange(this.UrgentlyHungryMood, -100f, this.HungryMood, "urgentlyHungryMood");
        RequireRange(this.StarvingMood, -100f, this.UrgentlyHungryMood, "starvingMood");
        RequireRange(this.UrgentlyHungrySpeedScale, 0.1f, 1f, "urgentlyHungrySpeedScale");
        RequireRange(this.StarvingSpeedScale, 0.1f, this.UrgentlyHungrySpeedScale, "starvingSpeedScale");
        RequireRange(this.MalnutritionGainPerMinute, 0.001f, 1f, "malnutritionGainPerMinute");
        RequireRange(this.MalnutritionRecoveryPerMinute, 0f, 1f, "malnutritionRecoveryPerMinute");
        RequireRange(this.StarvationDamageIntervalSeconds, 0.1f, 600f, "starvationDamageIntervalSeconds");
        RequireRange(this.StarvationDamageBase, 0f, 1_000f, "starvationDamageBase");
        RequireRange(this.StarvationDamagePerSeverity, 0f, 1_000f, "starvationDamagePerSeverity");
        RequireRange(this.RestMax, 1f, 10_000f, "restMax");
        RequireRange(this.RestDecayPerMinute, 0f, 1_000f, "restDecayPerMinute");
        RequireRange(this.RestRecoveryPerMinute, 0f, 10_000f, "restRecoveryPerMinute");
        RequireRange(this.TiredThreshold, 0.01f, 1f, "tiredThreshold");
        RequireRange(this.VeryTiredThreshold, 0.001f, this.TiredThreshold, "veryTiredThreshold");
        RequireRange(this.TiredMood, -100f, 0f, "tiredMood");
        RequireRange(this.VeryTiredMood, -100f, this.TiredMood, "veryTiredMood");
        RequireRange(this.ExhaustedMood, -100f, this.VeryTiredMood, "exhaustedMood");
        RequireRange(this.ExhaustedSpeedScale, 0.1f, 1f, "exhaustedSpeedScale");
        RequireRange(this.MoodBaseline, 0f, 100f, "moodBaseline");
        RequireRange(this.MiserableBelow, 0f, 100f, "miserableBelow");
        RequireRange(this.StressedBelow, this.MiserableBelow, 100f, "stressedBelow");
        RequireRange(this.ContentAtOrAbove, this.StressedBelow, 100f, "contentAtOrAbove");
        RequireRange(this.HappyAtOrAbove, this.ContentAtOrAbove, 100f, "happyAtOrAbove");
        RequireRange(this.PoisonDurationSeconds, 0f, 3_600f, "poisonDurationSeconds");
        RequireRange(this.PoisonMood, -100f, 0f, "poisonMood");
        RequireRange(this.PoisonSpeedScale, 0.1f, 1f, "poisonSpeedScale");

        if (this.FoodActivity is null || this.RestActivity is null)
        {
            throw new InvalidDataException("needs.json must define foodActivity and restActivity.");
        }

        this.FoodActivity.Validate("foodActivity");
        this.RestActivity.Validate("restActivity");
    }

    internal static void RequireRange(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
        {
            throw new InvalidDataException($"needs.json '{name}' = {value} is outside [{min}, {max}].");
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Survival.Config;

namespace Carbon.Survival.Components;

/// <summary>
/// Per-entity survival needs (food, rest, malnutrition, mood) and the gameplay effects derived from them.
/// Unmanaged struct component, written by <see cref="Systems.NeedsSystem"/> and <see cref="Systems.EatActionSystem"/>.
/// The simulation sets <see cref="Activity"/> every tick; every other member is owned by the survival systems.
/// </summary>
public struct NeedsComponent
{
    /// <summary>Number of timed mood memories an entity can hold at once.</summary>
    public const int MoodMemoryCapacity = 4;

    internal MoodMemoryBuffer Memories;

    /// <summary>Gets or sets the current food level, 0..<see cref="FoodMax"/>.</summary>
    public float Food { get; set; }

    /// <summary>Gets or sets the food capacity.</summary>
    public float FoodMax { get; set; }

    /// <summary>Gets or sets the current rest level, 0..<see cref="RestMax"/>.</summary>
    public float Rest { get; set; }

    /// <summary>Gets or sets the rest capacity.</summary>
    public float RestMax { get; set; }

    /// <summary>Gets or sets the malnutrition severity, 0..1. Reaching 1 is lethal.</summary>
    public float Malnutrition { get; set; }

    /// <summary>Gets or sets the mood value, 0..100.</summary>
    public float Mood { get; set; }

    /// <summary>Gets or sets the activity reported by the simulation for the current tick.</summary>
    public NeedsActivity Activity { get; set; }

    /// <summary>Gets or sets the current hunger threshold state.</summary>
    public HungerState Hunger { get; set; }

    /// <summary>Gets or sets the current rest threshold state.</summary>
    public RestState Fatigue { get; set; }

    /// <summary>Gets or sets the current mood band.</summary>
    public MoodState MoodBand { get; set; }

    /// <summary>Gets or sets the seconds accumulated toward the next starvation damage pulse.</summary>
    public float StarvationTimer { get; set; }

    /// <summary>Gets or sets the seconds of food poisoning left; zero when healthy.</summary>
    public float PoisonRemainingSeconds { get; set; }

    /// <summary>Gets or sets the movement speed multiplier from needs (1 = no penalty).</summary>
    public float MoveSpeedScale { get; set; }

    /// <summary>Gets or sets the attack speed multiplier from needs (1 = no penalty).</summary>
    public float AttackSpeedScale { get; set; }

    /// <summary>Gets or sets a value indicating whether natural health regeneration is suppressed.</summary>
    public bool HealthRegenBlocked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current sleep is in a comfortable spot (a bed in a roofed room).
    /// Set by the host while <see cref="Activity"/> is <see cref="NeedsActivity.Sleeping"/>.
    /// </summary>
    public bool SleepComfort { get; set; }

    /// <summary>Gets the food level as a 0..1 fraction.</summary>
    public readonly float FoodFraction => this.FoodMax > 0f ? Math.Clamp(this.Food / this.FoodMax, 0f, 1f) : 0f;

    /// <summary>Gets the rest level as a 0..1 fraction.</summary>
    public readonly float RestFraction => this.RestMax > 0f ? Math.Clamp(this.Rest / this.RestMax, 0f, 1f) : 0f;

    /// <summary>Gets a value indicating whether the entity has food poisoning.</summary>
    public readonly bool IsPoisoned => this.PoisonRemainingSeconds > 0f;

    /// <summary>
    /// Creates a fully fed and rested needs component at baseline mood.
    /// </summary>
    /// <param name="config">Needs tuning.</param>
    /// <returns>The initial component.</returns>
    public static NeedsComponent CreateFull(NeedsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new NeedsComponent
        {
            Food = config.FoodMax,
            FoodMax = config.FoodMax,
            Rest = config.RestMax,
            RestMax = config.RestMax,
            Mood = config.MoodBaseline,
            Activity = NeedsActivity.Idle,
            Hunger = HungerState.Fed,
            Fatigue = RestState.Rested,
            MoodBand = config.ClassifyMood(config.MoodBaseline),
            MoveSpeedScale = 1f,
            AttackSpeedScale = 1f,
        };
    }

    /// <summary>
    /// Reads a timed mood memory slot.
    /// </summary>
    /// <param name="index">Slot index, 0..<see cref="MoodMemoryCapacity"/>-1.</param>
    /// <returns>The memory in that slot; inactive slots have <see cref="MoodThought.None"/>.</returns>
    public readonly MoodMemory GetMemory(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MoodMemoryCapacity);
        return this.Memories[index];
    }

    /// <summary>
    /// Adds or refreshes a timed mood memory. A memory of the same kind is replaced; otherwise an empty slot
    /// is used, or the slot closest to expiring is overwritten.
    /// </summary>
    /// <param name="memory">Memory to add.</param>
    public void AddMemory(MoodMemory memory)
    {
        int target = -1;
        int weakest = 0;
        for (int i = 0; i < MoodMemoryCapacity; i++)
        {
            MoodMemory slot = this.Memories[i];
            if (slot.Thought == memory.Thought && slot.IsActive)
            {
                target = i;
                break;
            }

            if (target < 0 && !slot.IsActive)
            {
                target = i;
            }

            if (slot.RemainingSeconds < this.Memories[weakest].RemainingSeconds)
            {
                weakest = i;
            }
        }

        this.Memories[target >= 0 ? target : weakest] = memory;
    }
}

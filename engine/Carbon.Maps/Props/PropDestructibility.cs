// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Props;

/// <summary>
/// Unmanaged value object defining the destructibility, health durability, and harvesting parameters
/// of an environmental prop or map structure.
/// Maintains DDD purity: encapsulates prop health and tier without coupling to inventory/crafting systems.
/// </summary>
public readonly record struct PropDestructibility(
    bool IsDestructible,
    float MaxDurability,
    float CurrentDurability,
    byte HarvestTier,
    bool IsDestroyed)
{
    /// <summary>
    /// Gets a value indicating an indestructible prop, bedrock obstacle, or permanent ancient monument.
    /// </summary>
    public static PropDestructibility Indestructible => new(false, 0f, 0f, 0, false);

    /// <summary>
    /// Creates a destructible prop state with initial full durability and required harvest tool tier.
    /// </summary>
    /// <param name="durability">Total starting health/durability points.</param>
    /// <param name="harvestTier">Minimum tool tier required to harvest (0: hands, 1: wooden/stone, 2: iron, 3: steel/gem).</param>
    /// <returns>A new <see cref="PropDestructibility"/> instance.</returns>
    public static PropDestructibility Create(float durability, byte harvestTier = 1) =>
        new(true, durability, durability, harvestTier, false);

    /// <summary>
    /// Gets the remaining health percentage between 0.0 and 1.0.
    /// </summary>
    public float HealthPercentage =>
        this.MaxDurability > 0.001f ? Math.Clamp(this.CurrentDurability / this.MaxDurability, 0f, 1f) : 1f;

    /// <summary>
    /// Produces a modified state after suffering durability damage.
    /// </summary>
    /// <param name="damageAmount">Damage points inflicted.</param>
    /// <returns>Updated destructibility record.</returns>
    public PropDestructibility ApplyDamage(float damageAmount)
    {
        if (!this.IsDestructible || this.IsDestroyed)
        {
            return this;
        }

        float remaining = MathF.Max(0f, this.CurrentDurability - damageAmount);
        bool destroyed = remaining <= 0f;
        return new PropDestructibility(this.IsDestructible, this.MaxDurability, remaining, this.HarvestTier, destroyed);
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Physics.Obstacles;

/// <summary>
/// Terrain bits of a collider's collision mask (ADR-032). Every collider holds them all by default, which is a land
/// walker that wades: deep water blocks it (<see cref="DeepWater"/>) and dry land does not.
/// <list type="bullet">
/// <item>A swimmer (a player) clears <see cref="DeepWater"/> and may also enter Deep and Abyss water.</item>
/// <item>A water-bound creature clears <see cref="LandWalker"/>: dry land blocks it, and it may enter only the depth
/// tiers whose bit (<see cref="WaterTierBit"/>, Shore = 1 to Abyss = 5) is still set, deep ones included.</item>
/// </list>
/// The bits sit above the entity layers, which use the low bits.
/// </summary>
public static class TerrainMask
{
    /// <summary>Deep water (Deep and Abyss tiers, <c>DeepOcean</c> tiles) blocks the collider.</summary>
    public const uint DeepWater = 1U << 30;

    /// <summary>The collider may stand on dry land; cleared for a creature bound to water.</summary>
    public const uint LandWalker = 1U << 29;

    /// <summary>All five water tier bits (Shore to Abyss).</summary>
    public const uint WaterTiers = 0x1FU << FirstWaterTierBit;

    private const int FirstWaterTierBit = 24;

    /// <summary>Gets whether a collider with <paramref name="mask"/> can enter deep water.</summary>
    /// <param name="mask">The collider's collision mask.</param>
    /// <returns><c>true</c> for a swimmer.</returns>
    public static bool CanSwim(uint mask) => (mask & DeepWater) == 0;

    /// <summary>Gets whether a collider with <paramref name="mask"/> is bound to water (dry land blocks it).</summary>
    /// <param name="mask">The collider's collision mask.</param>
    /// <returns><c>true</c> for a water creature.</returns>
    public static bool IsWaterBound(uint mask) => (mask & LandWalker) == 0;

    /// <summary>Gets the bit of water depth tier <paramref name="tier"/> (1 = Shore … 5 = Abyss).</summary>
    /// <param name="tier">The tier, 1..5.</param>
    /// <returns>The bit; 0 for dry land (0) or an unknown tier.</returns>
    public static uint WaterTierBit(int tier) => tier is >= 1 and <= 5 ? 1U << (FirstWaterTierBit + tier - 1) : 0U;

    /// <summary>
    /// Gets whether a collider with <paramref name="mask"/> may stand in water of depth tier <paramref name="tier"/>
    /// (0 = dry land). A land walker may stand on land and in shallow water, and in deep water only as a swimmer.
    /// </summary>
    /// <param name="mask">The collider's collision mask.</param>
    /// <param name="tier">The depth tier, 0 (dry) to 5 (Abyss).</param>
    /// <returns><c>true</c> when the tier is open to the collider.</returns>
    public static bool AllowsWaterTier(uint mask, int tier)
    {
        if (!IsWaterBound(mask))
        {
            return tier < 4 || CanSwim(mask);
        }

        return (mask & WaterTierBit(tier)) != 0;
    }

    /// <summary>
    /// Gets <paramref name="mask"/> bound to water tiers <paramref name="minTier"/>..<paramref name="maxTier"/>
    /// (1 = Shore … 5 = Abyss): dry land and the other tiers block it. A water-bound mask reads only its tier bits, so
    /// <see cref="DeepWater"/> plays no part.
    /// </summary>
    /// <param name="mask">The collider's collision mask.</param>
    /// <param name="minTier">Shallowest tier, 1..5.</param>
    /// <param name="maxTier">Deepest tier, <paramref name="minTier"/>..5.</param>
    /// <returns>The water-bound mask.</returns>
    public static uint WaterBound(uint mask, int minTier, int maxTier)
    {
        uint tiers = 0U;
        for (int tier = minTier; tier <= maxTier; tier++)
        {
            tiers |= WaterTierBit(tier);
        }

        return (mask & ~(LandWalker | WaterTiers)) | tiers;
    }
}

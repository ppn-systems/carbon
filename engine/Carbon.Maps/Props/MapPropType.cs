// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Props;

/// <summary>
/// Identifies natural physical and decorative environmental props, trees, flora,
/// and mineral ore deposits scattered throughout procedural biomes.
/// </summary>
public enum MapPropType : byte
{
    /// <summary>
    /// No prop occupying this tile.
    /// </summary>
    None = 0,

    // --- Cây cối & Thực vật (Trees & Flora) ---

    /// <summary>
    /// Broadleaf oak tree with dense green canopy (Lush Plains, Ancient Forest).
    /// </summary>
    OakTree = 1,

    /// <summary>
    /// Coniferous pine tree with needle foliage (Highlands, Ancient Forest).
    /// </summary>
    PineTree = 2,

    /// <summary>
    /// Snow-dusted glacial pine tree (Frozen Tundra).
    /// </summary>
    SnowPine = 3,

    /// <summary>
    /// Tropical palm tree with slender trunk and fronds (Beach Sand, Tropical Jungle).
    /// </summary>
    PalmTree = 4,

    /// <summary>
    /// Slender white-barked birch tree with golden-green foliage (Lush Plains, Ancient Forest).
    /// </summary>
    BirchTree = 5,

    /// <summary>
    /// Gnarled weeping willow tree with drooping canopy (Misty Swamp, Riverbanks).
    /// </summary>
    WillowTree = 6,

    /// <summary>
    /// Gnarled, leafless swamp or desert tree (Misty Swamp, Volcanic Ashland, Dune Desert).
    /// </summary>
    DeadTree = 7,

    /// <summary>
    /// Multi-branched desert saguaro cactus (Dune Desert, Red Mesa).
    /// </summary>
    Cactus = 8,

    /// <summary>
    /// Low green foliage bush / shrub (Lush Plains, Forest undergrowth, Tropical Jungle).
    /// </summary>
    Bush = 9,

    /// <summary>
    /// Earthy mushroom cluster growing in shaded damp soil (Ancient Forest, Misty Swamp).
    /// </summary>
    MushroomCluster = 10,

    // --- Quặng & Khoáng sản (Ores & Minerals) ---

    /// <summary>
    /// Copper ore outcrop with verdigris greenish-bronze specks (Lush Plains, Red Mesa, Highlands).
    /// </summary>
    CopperVein = 11,

    /// <summary>
    /// Iron ore deposit with slate grey stone and rust-orange veins (Ancient Forest, Misty Swamp, Red Mesa).
    /// </summary>
    IronVein = 12,

    /// <summary>
    /// Precious gold ore outcrop with glistening yellow flakes (Red Mesa, Volcanic Ashland, Dune Desert).
    /// </summary>
    GoldVein = 13,

    /// <summary>
    /// Coal deposit with rough charcoal black facets (Ancient Forest, Highlands, Frozen Tundra).
    /// </summary>
    CoalDeposit = 14,

    /// <summary>
    /// Silver ore outcrop with bright blue-grey metallic sheen (Frozen Tundra, Red Mesa).
    /// </summary>
    SilverVein = 15,

    /// <summary>
    /// Luminous crystal / gem geode cluster (Volcanic Ashland, Frozen Tundra, Mystical Landmarks).
    /// </summary>
    CrystalCluster = 16,

    // --- Địa hình đá & Di tích (Rocks & Landmarks) ---

    /// <summary>
    /// Solid natural rock boulder obstacle (All land biomes).
    /// </summary>
    Boulder = 17,

    /// <summary>
    /// Impassable hexagonal basalt volcanic column (Volcanic Ashland).
    /// </summary>
    BasaltPillar = 18,

    /// <summary>
    /// Ancient mystical stone shrine obelisk (Wilderness POI).
    /// </summary>
    AncientShrine = 19,

    /// <summary>
    /// Wide umbrella-canopied acacia tree (Savanna).
    /// </summary>
    AcaciaTree = 20,

    /// <summary>
    /// Flowering cherry blossom tree with vibrant pink sakura canopy (Blossom Woods).
    /// </summary>
    CherryBlossomTree = 21,

    /// <summary>
    /// Faceted pale-cyan glacial ice crystal spire with perpetual frost glow (Alpine Peaks).
    /// </summary>
    GlacialCrystal = 22,

    /// <summary>
    /// Low wiry shrub of dry twigs and sparse leaves (Dune Desert, Red Mesa, Savanna open ground).
    /// </summary>
    DryBush = 23,

    /// <summary>
    /// Tall clump of marram grass binding the sand (Beach Sand dunes).
    /// </summary>
    DuneGrass = 24,

    /// <summary>
    /// A few loose stones lying on the ground, gathered by hand (spawn clearing starter forage).
    /// </summary>
    LooseStones = 25,

    /// <summary>
    /// A fallen branch lying on the ground, gathered by hand (spawn clearing starter forage).
    /// </summary>
    FallenBranch = 26,
}

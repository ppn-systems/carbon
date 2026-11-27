// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Creatures.Definitions;

/// <summary>
/// JSON model of a monster's tactical personality (the optional <c>behavior</c> block in <c>monsters.json</c>).
/// Every member has a conservative default so a monster without the block behaves like a plain brawler.
/// </summary>
public sealed class MonsterBehaviorDefinition
{
    /// <summary>Gets the combat positioning role.</summary>
    public MonsterRole Role { get; set; } = MonsterRole.Melee;

    /// <summary>
    /// Gets the distance ranged monsters and casters try to hold from the target; 0 = midpoint of
    /// <see cref="MonsterDefinition.RetreatRange"/> and <see cref="MonsterDefinition.EngageRange"/>.
    /// </summary>
    public float PreferredRange { get; set; }

    /// <summary>Gets the half width of the band around <see cref="PreferredRange"/> where the monster is content to strafe.</summary>
    public float RangeTolerance { get; set; } = 24f;

    /// <summary>Gets the strafe speed as a fraction of move speed while holding range; 0 disables strafing.</summary>
    public float StrafeSpeedFactor { get; set; }

    /// <summary>Gets the ticks between strafe direction flips.</summary>
    public uint StrafeSwitchTicks { get; set; } = 45;

    /// <summary>Gets the melee surround ring radius (must be below <see cref="MonsterDefinition.EngageRange"/>); 0 disables.</summary>
    public float SurroundRadius { get; set; }

    /// <summary>Gets the pack identifier; monsters with the same pack assist each other. Empty = solitary.</summary>
    public string? Pack { get; set; }

    /// <summary>Gets the radius in which pack allies answer a call for help.</summary>
    public float AssistRadius { get; set; }

    /// <summary>Gets the health fraction at or below which the monster flees once per engagement; 0 = never flees.</summary>
    public float FleeHealthFraction { get; set; }

    /// <summary>Gets the flee duration in ticks.</summary>
    public uint FleeTicks { get; set; }

    /// <summary>Gets the consecutive chase ticks without reaching the target before giving up and leashing; 0 = never.</summary>
    public uint GiveUpTicks { get; set; }

    /// <summary>Gets the threat-switch hysteresis factor (new leader must exceed the current target's threat by this factor).</summary>
    public float ThreatSwitchMargin { get; set; } = 1.1f;

    /// <summary>Gets the aggro and assist radius multiplier at night.</summary>
    public float NightAggroMultiplier { get; set; } = 1f;

    /// <summary>Gets the leash radius multiplier at night.</summary>
    public float NightLeashMultiplier { get; set; } = 1f;
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Hero attack motion picked from the held item and the combo step (<c>docs/art/combat-animation.md</c> §3.3). Each style
/// has one <c>base_hero.cvg.json</c> clip and one <see cref="HeroAttackTiming"/>.
/// </summary>
public enum HeroAttackStyle : byte
{
    /// <summary>Not a hero attack (skills, consumables, idle).</summary>
    None = 0,

    /// <summary>Bare hands, combo step 1: straight jab.</summary>
    Jab = 1,

    /// <summary>Bare hands, combo step 2: wide hook with a torso twist.</summary>
    Hook = 2,

    /// <summary>One-handed weapon, combo step 1: high-to-low diagonal slash.</summary>
    SlashForehand = 3,

    /// <summary>One-handed weapon, combo step 2: low-to-high backhand slash.</summary>
    SlashBackhand = 4,

    /// <summary>One-handed weapon, combo step 3: lunging thrust.</summary>
    Thrust = 5,

    /// <summary>Axe, pickaxe or building: overhead chop with a held apex.</summary>
    Overhead = 6,

    /// <summary>Bow: draw, hold, release.</summary>
    BowShot = 7,

    /// <summary>Staff: raise and push the focus toward the target.</summary>
    StaffCast = 8,
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Creatures.Definitions;

/// <summary>
/// Combat positioning archetype of a monster, read by <see cref="Systems.MonsterBrainSystem"/>.
/// </summary>
public enum MonsterRole : byte
{
    /// <summary>Closes in and fights at melee reach; surrounds the target with its pack.</summary>
    Melee = 0,

    /// <summary>Holds <see cref="MonsterBehaviorDefinition.PreferredRange"/>, backs off when approached and strafes between shots.</summary>
    Ranged = 1,

    /// <summary>Like <see cref="Ranged"/> but holds still to channel telegraphed spells; strafes slowly.</summary>
    Caster = 2
}

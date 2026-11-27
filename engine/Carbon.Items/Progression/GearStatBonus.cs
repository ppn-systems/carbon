// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Progression;

/// <summary>Summed stat bonuses of the equipped armor and the held weapon (enhancement already applied).</summary>
/// <param name="MaxHealth">Bonus max health.</param>
/// <param name="MaxMana">Bonus max mana.</param>
/// <param name="Defense">Armor defense (also feeds magic resist through <see cref="ProgressionConfig.ArmorMagicResistFraction"/>).</param>
/// <param name="CritChance">Held weapon critical chance.</param>
public readonly record struct GearStatBonus(float MaxHealth, float MaxMana, float Defense, float CritChance);

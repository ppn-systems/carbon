// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Entities;

/// <summary>
/// Shared wire encoding helper separating hotbar slot actions (<c>0..8</c>),
/// active skill activations (<c>100 + skillId</c>), and observer VFX cast codes
/// (<c>SkillCastBroadcast.AbilitySlot</c>).
/// </summary>
public static class AbilityInputEncoding
{
    /// <summary>Maximum valid hotbar slot index for standard primary actions.</summary>
    public const byte MaxHotbarSlot = 8;

    /// <summary>Offset added to active skill IDs (<c>1..109</c>) on <c>EntityAttackInput.AbilitySlot</c>.</summary>
    public const byte SkillCodeOffset = 100;

    /// <summary>Base VFX code reserved for player projectile casts (<c>210..219</c>).</summary>
    public const byte PlayerProjectileCastOffset = 210;

    /// <summary>Base VFX code reserved for monster projectile casts (<c>220..229</c>).</summary>
    public const byte MonsterProjectileCastOffset = 220;

    /// <summary>Encodes an active skill ID into an <c>EntityAttackInput.AbilitySlot</c> byte.</summary>
    public static byte EncodeSkill(ushort skillId) =>
        skillId is >= 1 and <= 109
            ? (byte)(SkillCodeOffset + skillId)
            : (byte)0;

    /// <summary>Attempts to decode an active skill ID (<c>1..109</c>) from <paramref name="abilityCode"/>.</summary>
    public static bool TryDecodeSkill(byte abilityCode, out ushort skillId)
    {
        if (abilityCode > SkillCodeOffset && abilityCode < PlayerProjectileCastOffset)
        {
            skillId = (ushort)(abilityCode - SkillCodeOffset);
            return true;
        }

        skillId = 0;
        return false;
    }

    /// <summary>Encodes a player ranged projectile cast VFX code for <paramref name="projectileArchetype"/> (<c>0..9</c>).</summary>
    public static byte EncodePlayerProjectileCast(byte projectileArchetype) =>
        (byte)(PlayerProjectileCastOffset + (projectileArchetype % 10));

    /// <summary>Encodes a monster ranged projectile cast VFX code for <paramref name="projectileArchetype"/> (<c>0..9</c>).</summary>
    public static byte EncodeMonsterProjectileCast(byte projectileArchetype) =>
        (byte)(MonsterProjectileCastOffset + (projectileArchetype % 10));

    /// <summary>Attempts to decode a player projectile archetype from <see cref="S2C.SkillCastBroadcast.AbilitySlot"/>.</summary>
    public static bool TryDecodePlayerProjectileCast(byte castCode, out byte projectileArchetype)
    {
        if (castCode >= PlayerProjectileCastOffset && castCode < MonsterProjectileCastOffset)
        {
            projectileArchetype = (byte)(castCode - PlayerProjectileCastOffset);
            return true;
        }

        projectileArchetype = 0;
        return false;
    }

    /// <summary>Attempts to decode a monster projectile archetype from <see cref="S2C.SkillCastBroadcast.AbilitySlot"/>.</summary>
    public static bool TryDecodeMonsterProjectileCast(byte castCode, out byte projectileArchetype)
    {
        if (castCode >= MonsterProjectileCastOffset && castCode < MonsterProjectileCastOffset + 10)
        {
            projectileArchetype = (byte)(castCode - MonsterProjectileCastOffset);
            return true;
        }

        projectileArchetype = 0;
        return false;
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Entities;

/// <summary>
/// Shared wire encoding helper separating hotbar slot actions (<c>0..8</c>) from
/// active skill activations encoded into <see cref="C2S.EntityAttackInput.AbilitySlot"/>.
/// </summary>
public static class AbilityInputEncoding
{
    /// <summary>Maximum valid hotbar slot index for standard primary actions.</summary>
    public const byte MaxHotbarSlot = 8;

    /// <summary>Offset added to active skill slots (<c>1..N</c>) on the wire.</summary>
    public const byte SkillCodeOffset = 100;

    /// <summary>Encodes a 1-based active skill slot into an <c>AbilitySlot</c> byte.</summary>
    public static byte EncodeSkill(byte skillSlot) => (byte)(SkillCodeOffset + skillSlot);

    /// <summary>Attempts to decode an active skill slot (<c>&gt;= 1</c>) from <paramref name="abilityCode"/>.</summary>
    public static bool TryDecodeSkill(byte abilityCode, out byte skillSlot)
    {
        if (abilityCode > SkillCodeOffset)
        {
            skillSlot = (byte)(abilityCode - SkillCodeOffset);
            return true;
        }

        skillSlot = 0;
        return false;
    }
}

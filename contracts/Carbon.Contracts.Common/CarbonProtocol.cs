// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Common;

/// <summary>
/// Global Carbon wire protocol metadata shared across all modules.
/// </summary>
public static class CarbonProtocol
{
    /// <summary>
    /// Current wire protocol version negotiated during authentication. 2: <c>CharacterCreate</c> no longer carries a
    /// starting class (ADR-030). 3: <c>StatusEffectSync</c> carries a level per entry and <c>CombatHit</c> the pulsing
    /// effect's level (ADR-036). 4: the dead opcodes <c>0x1101</c>, <c>0x1110</c>,
    /// <c>0x1130</c>, <c>0x4007</c>, <c>0x4008</c>, <c>0x4041</c> and <c>0x4042</c> are retired (ADR-038). 5: the never-read fields of ADR-038
    /// batch 2 leave their packets. 6: <c>EntityDeltaBatch</c> no longer carries <c>DirtyMasks</c>
    /// (ADR-038 batch 3).
    /// </summary>
    public const ushort Version = 6;
}

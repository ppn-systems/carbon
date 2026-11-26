// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Entities;

/// <summary>
/// Identifies whether a progression point spend targets an attribute or an active/passive skill.
/// </summary>
public enum PointAllocationKind : byte
{
    /// <summary>Primary character attribute identified by <see cref="PrimaryAttributeId"/>.</summary>
    Attribute = 0,

    /// <summary>Skill mastery unlock or rank upgrade identified by numeric <c>SkillId</c>.</summary>
    Skill = 1,
}

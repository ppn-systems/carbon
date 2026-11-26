// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Contracts.Entities;

namespace Carbon.Items.Catalog;

/// <summary>
/// Mutable, extensible character attribute &amp; skill mastery progression container replacing fixed
/// <c>Skill1..Skill3</c> and <c>Vit..Agi</c> positional scalars.
/// </summary>
public sealed class CharacterProgressionState
{
    /// <summary>Maximum attribute slot count backed directly in a flat array for zero-alloc hot-path reads.</summary>
    public const int MaxAttributeSlots = 16;

    private readonly ushort[] _attributePoints = new ushort[MaxAttributeSlots];
    private readonly Dictionary<ushort, byte> _skillRanks = new(8);

    /// <summary>Gets or sets unlocked skill bitmask (<c>1..64</c>).</summary>
    public CharacterSkillMask UnlockedSkillMask { get; private set; }

    /// <summary>Returns allocated points for <paramref name="attr"/>.</summary>
    public ushort GetAttribute(PrimaryAttributeId attr)
    {
        int idx = (int)attr;
        return (uint)idx < MaxAttributeSlots ? _attributePoints[idx] : (ushort)0;
    }

    /// <summary>Sets allocated points for <paramref name="attr"/>.</summary>
    public void SetAttribute(PrimaryAttributeId attr, ushort points)
    {
        int idx = (int)attr;
        if ((uint)idx < MaxAttributeSlots)
        {
            _attributePoints[idx] = points;
        }
    }

    /// <summary>Adds <paramref name="delta"/> points to <paramref name="attr"/> with saturation.</summary>
    public void AddAttribute(PrimaryAttributeId attr, ushort delta)
    {
        int idx = (int)attr;
        if ((uint)idx < MaxAttributeSlots)
        {
            int next = Math.Min(ushort.MaxValue, _attributePoints[idx] + delta);
            _attributePoints[idx] = (ushort)next;
        }
    }

    /// <summary>Gets total attribute points spent across all attributes.</summary>
    public int GetTotalSpentAttributePoints()
    {
        int sum = 0;
        for (int i = 0; i < MaxAttributeSlots; i++)
        {
            sum += _attributePoints[i];
        }

        return sum;
    }

    /// <summary>Gets learned mastery rank for <paramref name="skillId"/> (<c>0</c> = locked).</summary>
    public byte GetSkillRank(ushort skillId) =>
        _skillRanks.TryGetValue(skillId, out byte rank) ? rank : (byte)0;

    /// <summary>Returns whether <paramref name="skillId"/> has at least rank <c>1</c>.</summary>
    public bool IsSkillUnlocked(ushort skillId) =>
        this.GetSkillRank(skillId) > 0 || CharacterSkillMaskHelper.HasSkill(this.UnlockedSkillMask, skillId);

    /// <summary>Sets learned mastery rank for <paramref name="skillId"/> and synchronizes <see cref="UnlockedSkillMask"/>.</summary>
    public void SetSkillRank(ushort skillId, byte rank)
    {
        CharacterSkillMask bit = CharacterSkillMaskHelper.BitForSkill(skillId);
        if (rank == 0)
        {
            _ = _skillRanks.Remove(skillId);
            this.UnlockedSkillMask &= ~bit;
            return;
        }

        _skillRanks[skillId] = rank;
        this.UnlockedSkillMask |= bit;
    }

    /// <summary>Gets total skill points spent across all learned skills.</summary>
    public int GetTotalSpentSkillPoints()
    {
        int sum = 0;
        foreach (KeyValuePair<ushort, byte> kvp in _skillRanks)
        {
            sum += kvp.Value;
        }

        return sum;
    }

    /// <summary>Enumerates all non-zero attribute allocations (<c>AttributeId, Points</c>).</summary>
    public void CopyNonZeroAttributes(List<(PrimaryAttributeId Id, ushort Points)> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        for (int i = 0; i < MaxAttributeSlots; i++)
        {
            if (_attributePoints[i] > 0)
            {
                destination.Add(((PrimaryAttributeId)i, _attributePoints[i]));
            }
        }
    }

    /// <summary>Enumerates all learned skills (<c>SkillId, Rank</c>).</summary>
    public void CopyLearnedSkills(List<(ushort SkillId, byte Rank)> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        foreach (KeyValuePair<ushort, byte> kvp in _skillRanks)
        {
            if (kvp.Value > 0)
            {
                destination.Add((kvp.Key, kvp.Value));
            }
        }
    }

    /// <summary>Resets all attribute allocations, skill ranks, and unlock flags to empty.</summary>
    public void Clear()
    {
        Array.Clear(_attributePoints);
        _skillRanks.Clear();
        this.UnlockedSkillMask = CharacterSkillMask.None;
    }

    /// <summary>Copies all state into <paramref name="destination"/> without mutating source.</summary>
    public void CopyTo(CharacterProgressionState destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Array.Copy(_attributePoints, destination._attributePoints, MaxAttributeSlots);
        destination._skillRanks.Clear();
        foreach (KeyValuePair<ushort, byte> kvp in _skillRanks)
        {
            destination._skillRanks[kvp.Key] = kvp.Value;
        }

        destination.UnlockedSkillMask = this.UnlockedSkillMask;
    }

    /// <summary>Forces synchronization of <see cref="UnlockedSkillMask"/> from loaded skill ranks.</summary>
    public void SetUnlockedSkillMask(CharacterSkillMask mask) =>
        this.UnlockedSkillMask = mask;
}

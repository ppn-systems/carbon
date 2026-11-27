// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;

namespace Carbon.Items.Serialization;

/// <summary>
/// Compact little-endian binary serializer for <see cref="CharacterProgressionState"/> payloads stored
/// in DB blobs and wire sync frames. Uses explicit fixed-width framing compatible with Native AOT.
/// </summary>
public static class CharacterProgressionBinaryCodec
{
    private const byte FormatVersion = 1;
    private const int HeaderBytes = 12; // Version(1) + UnlockedSkillMask(8) + AttrCount(1) + SkillCount(2)

    /// <summary>Serializes <paramref name="state"/> into a compact little-endian byte payload.</summary>
    public static byte[] Serialize(CharacterProgressionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        int attrCount = 0;
        for (int i = 0; i < CharacterProgressionState.MaxAttributeSlots; i++)
        {
            if (state.GetAttribute((PrimaryAttributeId)i) > 0)
            {
                attrCount++;
            }
        }

        // Same filter and order as CharacterProgressionState.CopyLearnedSkills, without the intermediate list.
        int skillCount = 0;
        foreach (KeyValuePair<ushort, byte> kvp in state.SkillRanks)
        {
            if (kvp.Value > 0)
            {
                skillCount++;
            }
        }

        int totalBytes = HeaderBytes + (attrCount * 3) + (skillCount * 3);
        byte[] buffer = new byte[totalBytes];
        Span<byte> span = buffer;

        span[0] = FormatVersion;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(1, 8), (ulong)state.UnlockedSkillMask);
        span[9] = (byte)attrCount;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10, 2), (ushort)skillCount);

        int offset = HeaderBytes;
        for (int i = 0; i < CharacterProgressionState.MaxAttributeSlots; i++)
        {
            ushort pts = state.GetAttribute((PrimaryAttributeId)i);
            if (pts == 0)
            {
                continue;
            }

            span[offset] = (byte)i;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset + 1, 2), pts);
            offset += 3;
        }

        foreach (KeyValuePair<ushort, byte> kvp in state.SkillRanks)
        {
            if (kvp.Value == 0)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), kvp.Key);
            span[offset + 2] = kvp.Value;
            offset += 3;
        }

        return buffer;
    }

    /// <summary>
    /// Returns the serialized payload of <paramref name="state"/>, reusing the array built for the previous call while
    /// <see cref="CharacterProgressionState.Revision"/> is unchanged. For the periodic stats sync, which would otherwise
    /// re-serialize an unchanged state for every player every few ticks.
    /// </summary>
    /// <remarks>The returned array is shared between calls: treat it as read-only. Use <see cref="Serialize"/> for a private copy.</remarks>
    public static byte[] GetOrSerializeShared(CharacterProgressionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        byte[]? cached = state.CachedBlob;
        if (cached is not null && state.CachedBlobRevision == state.Revision)
        {
            return cached;
        }

        byte[] blob = Serialize(state);
        state.CachedBlob = blob;
        state.CachedBlobRevision = state.Revision;
        return blob;
    }

    /// <summary>
    /// Deserializes <paramref name="data"/> into <paramref name="target"/>.
    /// Empty spans clear <paramref name="target"/> and return <see langword="true"/>.
    /// </summary>
    public static bool TryDeserializeInto(ReadOnlySpan<byte> data, CharacterProgressionState target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.Clear();
        if (data.IsEmpty)
        {
            return true;
        }

        if (data.Length < HeaderBytes || data[0] != FormatVersion)
        {
            return false;
        }

        ulong maskRaw = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(1, 8));
        int attrCount = data[9];
        int skillCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10, 2));
        int expectedLength = HeaderBytes + (attrCount * 3) + (skillCount * 3);
        if (data.Length < expectedLength)
        {
            return false;
        }

        target.SetUnlockedSkillMask((CharacterSkillMask)maskRaw);

        int offset = HeaderBytes;
        for (int i = 0; i < attrCount; i++)
        {
            PrimaryAttributeId attrId = (PrimaryAttributeId)data[offset];
            ushort pts = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 1, 2));
            offset += 3;
            target.SetAttribute(attrId, pts);
        }

        for (int i = 0; i < skillCount; i++)
        {
            ushort skillId = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            byte rank = data[offset + 2];
            offset += 3;
            target.SetSkillRank(skillId, rank);
        }

        return true;
    }
}

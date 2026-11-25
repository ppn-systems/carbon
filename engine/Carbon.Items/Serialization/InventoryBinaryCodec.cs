// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using Carbon.Items.Containers;

namespace Carbon.Items.Serialization;

/// <summary>
/// Compact little-endian binary serializer for <see cref="InventoryContainer"/> payloads stored in character blobs.
/// Uses explicit fixed-width framing compatible with Native AOT and trimming.
/// </summary>
public static class InventoryBinaryCodec
{
    private const byte FormatVersion = 1;
    private const int HeaderBytes = 5;
    private const int EntryBytes = 22;

    /// <summary>
    /// Serializes all non-empty slots in <paramref name="container"/> into a binary payload.
    /// </summary>
    /// <param name="container">Source inventory container.</param>
    /// <returns>Serialized binary payload with 5-byte header.</returns>
    public static byte[] Serialize(InventoryContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);

        ReadOnlySpan<ItemSlot> slots = container.Slots;
        ushort occupied = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].IsEmpty)
            {
                occupied++;
            }
        }

        byte[] buffer = new byte[HeaderBytes + (occupied * EntryBytes)];
        Span<byte> span = buffer;
        span[0] = FormatVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(1, 2), (ushort)Math.Clamp(slots.Length, 0, ushort.MaxValue));
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(3, 2), occupied);

        int offset = HeaderBytes;
        for (int i = 0; i < slots.Length; i++)
        {
            ItemSlot slot = slots[i];
            if (slot.IsEmpty)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), (ushort)i);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset + 2, 4), slot.ItemId);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset + 6, 4), slot.Quantity);
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset + 10, 8), slot.InstanceId);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset + 18, 4), BitConverter.SingleToInt32Bits(slot.Durability));
            offset += EntryBytes;
        }

        return buffer;
    }

    /// <summary>
    /// Attempts to deserialize <paramref name="data"/> into <paramref name="target"/>.
    /// Returns <see langword="false"/> for empty or unrecognized payloads (such as brand-new characters).
    /// </summary>
    /// <param name="data">Raw persisted inventory bytes.</param>
    /// <param name="target">Destination container cleared and populated when valid.</param>
    /// <returns><see langword="true"/> if valid inventory data was loaded; otherwise, <see langword="false"/>.</returns>
    public static bool TryDeserializeInto(ReadOnlySpan<byte> data, InventoryContainer target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (data.Length < HeaderBytes || data[0] != FormatVersion)
        {
            return false;
        }

        ushort entryCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(3, 2));
        if (data.Length < HeaderBytes + (entryCount * EntryBytes))
        {
            return false;
        }

        target.Clear();
        int offset = HeaderBytes;
        for (int i = 0; i < entryCount; i++)
        {
            ushort slotIndex = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            uint itemId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 2, 4));
            int quantity = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 6, 4));
            ulong instanceId = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 10, 8));
            float durability = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 18, 4)));
            offset += EntryBytes;

            if (slotIndex < target.Capacity && itemId > 0 && quantity > 0)
            {
                target.SetSlot(slotIndex, new ItemSlot(itemId, quantity, instanceId, durability));
            }
        }

        return true;
    }
}

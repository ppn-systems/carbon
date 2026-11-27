// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.IO;
using Carbon.Contracts.Identity;

namespace Carbon.Items.Races;

/// <summary>
/// Versioned binary form of a <see cref="CharacterLook"/> for the <c>characters.appearance</c> column (ADR-042).
/// Layout v1: <c>[version=1][raceId u16 LE][bodyType u8][optionCount u8][option u8 × count]</c>.
/// </summary>
public static class CharacterLookBinaryCodec
{
    /// <summary>Current layout version.</summary>
    public const byte CurrentVersion = 1;

    private const int HeaderLength = 5;

    /// <summary>Serializes <paramref name="look"/>.</summary>
    /// <param name="look">Look to store.</param>
    /// <returns>The stored bytes.</returns>
    public static byte[] Serialize(in CharacterLook look)
    {
        byte[] buffer = new byte[HeaderLength + look.OptionCount];
        buffer[0] = CurrentVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(1, 2), look.RaceId);
        buffer[3] = look.BodyType;
        buffer[4] = look.OptionCount;
        look.CopyOptionsTo(buffer.AsSpan(HeaderLength));
        return buffer;
    }

    /// <summary>Deserializes a stored look.</summary>
    /// <param name="data">Stored bytes.</param>
    /// <returns>The look.</returns>
    /// <exception cref="InvalidDataException">Thrown when the bytes are not a v1 look.</exception>
    public static CharacterLook Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength || data[0] != CurrentVersion)
        {
            throw new InvalidDataException($"Stored character look has an unknown layout (length {data.Length}).");
        }

        int count = data[4];
        if (count > CharacterLook.MaxOptions || data.Length != HeaderLength + count)
        {
            throw new InvalidDataException($"Stored character look declares {count} options in {data.Length} bytes.");
        }

        return new CharacterLook(BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(1, 2)), data[3], data.Slice(HeaderLength, count));
    }
}

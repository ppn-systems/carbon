// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Common;

/// <summary>
/// Carbon shared cross-module protocol opcodes.
/// <para>Range <c>0x00F0–0x00FF</c> (top of the System band) is reserved for shared / utility packets.</para>
/// </summary>
public static class SharedOpCode
{
    /// <summary>Generic result returned by every write handler (<see cref="MutationResult"/>).</summary>
    public const ushort MutationResult = 0x00F0;
}

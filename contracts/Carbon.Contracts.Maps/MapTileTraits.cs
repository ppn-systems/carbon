// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Contracts.Maps;

/// <summary>Bit traits packed into one byte per tile on the wire.</summary>
[Flags]
public enum MapTileTraits : byte
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The tile blocks movement.</summary>
    Solid = 1 << 0,
}

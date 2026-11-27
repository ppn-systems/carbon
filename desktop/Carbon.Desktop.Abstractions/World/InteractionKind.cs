// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.World;

/// <summary>What the interact key does to a target. Append only.</summary>
public enum InteractionKind : byte
{
    /// <summary>Nothing in reach.</summary>
    None = 0,

    /// <summary>Pick up a ground loot drop.</summary>
    PickUp = 1,

    /// <summary>Talk to a trader (opens the trade window).</summary>
    Trade = 2,

    /// <summary>Open a closed door.</summary>
    OpenDoor = 3,

    /// <summary>Close an open door.</summary>
    CloseDoor = 4,

    /// <summary>Use a forge (an anvil, or a forge artisan who does not trade).</summary>
    Forge = 5,
}

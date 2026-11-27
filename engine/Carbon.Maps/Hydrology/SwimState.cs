// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Hydrology;

/// <summary>How a player moves through the water under them, from its depth tier (ADR-032 D10).</summary>
public enum SwimState : byte
{
    /// <summary>On dry land, a bridge or a pier.</summary>
    Walking = 0,

    /// <summary>Shore or Shallow water: wading.</summary>
    Wading = 1,

    /// <summary>Medium water: swimming on the surface.</summary>
    Swimming = 2,

    /// <summary>Deep or Abyss water: diving, using up oxygen.</summary>
    Diving = 3,
}

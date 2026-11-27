// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Items.Catalog;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>Resolves what a replicated NPC archetype offers (trading, forging), as the session knows it.</summary>
public interface INpcCapabilityLookup
{
    /// <summary>Gets the capabilities of NPC archetype <paramref name="archetypeId"/> (<see cref="NpcCapabilities.None"/> when unknown).</summary>
    NpcCapabilities GetNpcCapabilities(ushort archetypeId);
}

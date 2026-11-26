// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Items.Containers;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.Desktop.Abstractions;

/// <summary>
/// Strictly read-only world, actor vitals, inventory, action, and transient feedback query port (CQRS / ISP).
/// Renderers and HUD widgets depend only on this interface and cannot mutate network or gameplay state.
/// </summary>
public interface IClientWorldReadModel : IClientConnectionStatus, IClientCharacterProfile
{
    /// <summary>Gets the chunk world coordinator backing terrain, props, and structures.</summary>
    ChunkWorld2D ChunkWorld { get; }

    /// <summary>Gets the client-side fixed-tick simulation engine.</summary>
    WorldSimulation Simulation { get; }

    /// <summary>Gets the local player's ECS entity handle.</summary>
    Entity LocalPlayer { get; }

    /// <summary>Gets the frame-interpolated local player world position (with prediction smoothing).</summary>
    Vector2 LocalPlayerInterpolatedPosition { get; }

    /// <summary>Gets the local player's current health.</summary>
    float LocalPlayerHealth { get; }

    /// <summary>Gets the local player's maximum health.</summary>
    float LocalPlayerMaxHealth { get; }

    /// <summary>Gets the active zero-based hotbar slot index (<c>0..8</c>).</summary>
    byte SelectedHotbarSlot { get; }

    /// <summary>Gets the local player's active inventory container.</summary>
    InventoryContainer Inventory { get; }

    /// <summary>Gets the normalized direction vector of the most recent swing/attack.</summary>
    Vector2 LastAttackDirection { get; }

    /// <summary>Gets remaining visual swing arc timer in seconds (&gt; 0 while swinging).</summary>
    float AttackSwingTimer { get; }

    /// <summary>Gets the normalized facing direction of the local player.</summary>
    Vector2 LocalFacingDirection { get; }

    /// <summary>Gets remaining white hit-flash seconds when the local player takes damage.</summary>
    float LocalHitFlashTimer { get; }

    /// <summary>Gets the semantic visual action state for vector rig animation and VFX selection.</summary>
    ClientActionState CurrentAction { get; }

    /// <summary>Gets the remote players, monsters, and NPCs currently visible in the Area of Interest.</summary>
    IReadOnlyList<RemoteEntityView> RemoteEntities { get; }

    /// <summary>Gets a value indicating whether the world and local avatar are initialized.</summary>
    bool IsReady { get; }

    /// <summary>Gets the active world seed.</summary>
    ulong WorldSeed { get; }

    /// <summary>Drains newly buffered floating combat/loot events into <paramref name="destination"/>.</summary>
    void DrainCombatEvents(List<ClientCombatEvent> destination);

    /// <summary>Drains newly buffered prop impact/fracture events into <paramref name="destination"/>.</summary>
    void DrainPropImpactEvents(List<ClientPropImpactEvent> destination);

    /// <summary>Drains chunk coordinates invalidated by incoming network prop/structure/delta updates.</summary>
    void DrainDirtyChunks(List<(int ChunkX, int ChunkY)> destination);

    /// <summary>Drains recent loot, craft, or warning toast notifications for the HUD feed.</summary>
    void DrainHudNotifications(List<string> destination);
}

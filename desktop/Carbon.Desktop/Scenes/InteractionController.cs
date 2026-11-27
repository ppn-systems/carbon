// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Items.Catalog;
using Carbon.Maps.Building;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// The interact key (E). Once per frame <see cref="Refresh"/> collects everything the key could use (loot drops, traders,
/// doors, forges) and keeps the nearest one as <see cref="Current"/>. <see cref="BuildPrompt"/> draws the prompt for
/// <see cref="Current"/> and <see cref="TryInteract"/> acts on <see cref="Current"/>, so the two can never disagree. The
/// server validates every request. Allocation-free per frame.
/// </summary>
/// <param name="session">World session.</param>
internal sealed class InteractionController(IWorldSession session)
{
    /// <summary>Most candidates looked at in one frame (entities and structures together).</summary>
    public const int MaxCandidates = 96;

    /// <summary>UI: distance in world pixels within which a ground drop is picked up (the server re-checks).</summary>
    private const float PickupRadius = 84f;

    /// <summary>UI: distance in world pixels within which a trader can be talked to (the server re-checks 175 px).</summary>
    private const float TalkRadius = 150f;

    /// <summary>UI: distance in world pixels within which a forge (anvil or forge artisan) can be used.</summary>
    private const float ForgeRadius = 175f;

    /// <summary>UI: distance in tiles from the player to a door's centre within which it can be opened or closed.</summary>
    private const float DoorRadiusTiles = 2.5f;

    /// <summary>UI: height in screen pixels of the prompt box (it hangs below an entity target).</summary>
    private const float PromptHeightPixels = 26f;

    private readonly IWorldSession _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>Gets the target of the interact key this frame (also what the prompt shows).</summary>
    public InteractionTarget Current { get; private set; }

    /// <summary>Collects the candidates around the player and keeps the nearest usable one as <see cref="Current"/>.</summary>
    /// <returns>The new <see cref="Current"/>.</returns>
    public InteractionTarget Refresh()
    {
        if (!_session.IsReady)
        {
            this.Current = InteractionTarget.None;
            return this.Current;
        }

        Span<InteractionCandidate> buffer = stackalloc InteractionCandidate[MaxCandidates];
        int count = this.Collect(buffer, doorsOnly: false);
        this.Current = InteractionTargeting.SelectNearest(_session.LocalPlayerInterpolatedPosition, buffer[..count]);
        return this.Current;
    }

    /// <summary>
    /// Builds the prompt for <see cref="Current"/>: under an NPC or a ground drop (so it never covers the name tag), just
    /// above a door or an anvil. <see langword="null"/> when nothing is in reach.
    /// </summary>
    /// <param name="worldToScreen">Projects a world position to the screen (the scene's camera).</param>
    /// <returns>The prompt, or <see langword="null"/>.</returns>
    public InteractionPrompt? BuildPrompt(Func<Vector2, Vector2> worldToScreen)
    {
        ArgumentNullException.ThrowIfNull(worldToScreen);
        InteractionTarget target = this.Current;
        if (!target.Exists)
        {
            return null;
        }

        float tile = _session.ChunkWorld.TileSize;
        bool entity = target.NetworkId != 0UL;
        Vector2 screen = worldToScreen(target.Position + new Vector2(0f, entity ? 0.55f * tile : -0.6f * tile));
        return new InteractionPrompt(target.Kind, entity ? screen + new Vector2(0f, PromptHeightPixels) : screen);
    }

    /// <summary>Acts on <see cref="Current"/>.</summary>
    /// <param name="openForge">Opens the forge window (the scene owns it).</param>
    /// <returns><see langword="true"/> when a request was sent or a window opened.</returns>
    public bool TryInteract(Func<bool> openForge)
    {
        ArgumentNullException.ThrowIfNull(openForge);
        InteractionTarget target = this.Current;
        return target.Kind switch
        {
            InteractionKind.PickUp => _session.TryPickupLoot(target.NetworkId),
            InteractionKind.Trade => _session.Trading.TryOpen(target.NetworkId),
            InteractionKind.OpenDoor or InteractionKind.CloseDoor => _session.TryToggleDoor(target.TileX, target.TileY),
            InteractionKind.Forge => openForge(),
            InteractionKind.None => false,
            _ => false,
        };
    }

    /// <summary>The door key: opens or closes the nearest door in reach, whatever else is nearer.</summary>
    /// <returns><see langword="true"/> when a request was sent.</returns>
    public bool TryToggleNearestDoor()
    {
        if (!_session.IsReady)
        {
            return false;
        }

        Span<InteractionCandidate> buffer = stackalloc InteractionCandidate[MaxCandidates];
        int count = this.Collect(buffer, doorsOnly: true);
        InteractionTarget door = InteractionTargeting.SelectNearest(_session.LocalPlayerInterpolatedPosition, buffer[..count]);
        return door.Exists && _session.TryToggleDoor(door.TileX, door.TileY);
    }

    private int Collect(Span<InteractionCandidate> output, bool doorsOnly)
    {
        int count = 0;
        Vector2 player = _session.LocalPlayerInterpolatedPosition;
        if (!doorsOnly)
        {
            IReadOnlyList<RemoteEntityView> remotes = _session.RemoteEntities;
            for (int i = 0; i < remotes.Count && count < output.Length; i++)
            {
                RemoteEntityView e = remotes[i];
                (InteractionKind kind, float radius) = this.Classify(in e);
                if (kind != InteractionKind.None && Vector2.DistanceSquared(player, e.Position) <= radius * radius)
                {
                    output[count++] = new InteractionCandidate(new InteractionTarget(kind, e.Position, e.NetworkId, 0, 0), radius);
                }
            }
        }

        float tile = _session.ChunkWorld.TileSize;
        float doorRadius = DoorRadiusTiles * tile;
        int span = (int)MathF.Ceiling((doorsOnly ? doorRadius : MathF.Max(doorRadius, ForgeRadius)) / tile);
        int ptx = (int)MathF.Floor(player.X / tile);
        int pty = (int)MathF.Floor(player.Y / tile);
        BuildingGrid buildings = _session.ChunkWorld.Buildings;
        for (int dy = -span; dy <= span; dy++)
        {
            for (int dx = -span; dx <= span && count < output.Length; dx++)
            {
                int x = ptx + dx;
                int y = pty + dy;
                if (!buildings.TryGetStructure(x, y, out StructureTile st) || st.IsDestroyed)
                {
                    continue;
                }

                Vector2 centre = new((x + 0.5f) * tile, (y + 0.5f) * tile);
                if (st.Category == StructureCategory.Door)
                {
                    InteractionKind kind = st.DoorState == DoorState.Closed ? InteractionKind.OpenDoor : InteractionKind.CloseDoor;
                    output[count++] = new InteractionCandidate(new InteractionTarget(kind, centre, 0UL, x, y), doorRadius);
                }
                else if (!doorsOnly && st.Type == StructureType.ForgeAnvil)
                {
                    output[count++] = new InteractionCandidate(new InteractionTarget(InteractionKind.Forge, centre, 0UL, x, y), ForgeRadius);
                }
            }
        }

        return count;
    }

    private (InteractionKind Kind, float Radius) Classify(in RemoteEntityView e)
    {
        if (e.Kind == EntityKind.LootDrop)
        {
            return e.IsLootLocked ? (InteractionKind.None, 0f) : (InteractionKind.PickUp, PickupRadius);
        }

        if (e.Kind != EntityKind.Npc)
        {
            return (InteractionKind.None, 0f);
        }

        // A trading blacksmith trades on E (its forge stays on U); a forge artisan who does not trade opens the forge.
        NpcCapabilities caps = _session.GetNpcCapabilities(e.ArchetypeId);
        return caps.HasFlag(NpcCapabilities.Trader) ? (InteractionKind.Trade, TalkRadius)
            : caps.HasFlag(NpcCapabilities.ForgeArtisan) ? (InteractionKind.Forge, ForgeRadius)
            : (InteractionKind.None, 0f);
    }
}

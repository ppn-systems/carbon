// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.World;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// The local player's death and respawn request (ADR-031). The server's <see cref="PlayerDeath"/> push opens the death
/// state, and <see cref="TryRequestRespawn"/> sends one <c>RespawnRequest</c> at a time. When the server accepts it the
/// state closes; when it refuses, the player may ask again. Packet callbacks and the render thread both touch it, so
/// every member is thread-safe.
/// </summary>
/// <param name="sendRespawnRequest">Sends a <c>RespawnRequest</c> and completes with the server's <c>MutationResult</c> status.</param>
/// <param name="resolveMonsterNameKey">Maps a monster archetype id to its name's localization key (<see langword="null"/> when unknown).</param>
internal sealed class DeathSession(Func<Task<OpStatus>> sendRespawnRequest, Func<ushort, string?> resolveMonsterNameKey)
{
    private readonly Func<Task<OpStatus>> _send = sendRespawnRequest ?? throw new ArgumentNullException(nameof(sendRespawnRequest));
    private readonly Func<ushort, string?> _monsterNameKey = resolveMonsterNameKey ?? throw new ArgumentNullException(nameof(resolveMonsterNameKey));
    private readonly Lock _gate = new();
    private DeathView? _death;
    private bool _pending;

    /// <summary>Gets the current death, or <see langword="null"/> while alive.</summary>
    public DeathView? Death
    {
        get
        {
            lock (_gate)
            {
                return _death;
            }
        }
    }

    /// <summary>Gets whether a respawn request is waiting for the server's answer.</summary>
    public bool IsRespawnPending
    {
        get
        {
            lock (_gate)
            {
                return _pending;
            }
        }
    }

    /// <summary>Opens the death state from the server's push.</summary>
    public void OnPlayerDeath(PlayerDeath packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        string? killerKey = packet.Cause == DeathCause.Monster ? _monsterNameKey(packet.KillerArchetypeId) : null;
        lock (_gate)
        {
            _death = new DeathView(packet.Cause, packet.KillerArchetypeId, killerKey);
            _pending = false;
        }
    }

    /// <summary>Sends a respawn request when dead and none is waiting.</summary>
    /// <returns><see langword="true"/> when a request was sent.</returns>
    public bool TryRequestRespawn()
    {
        lock (_gate)
        {
            if (_death is null || _pending)
            {
                return false;
            }

            _pending = true;
        }

        _ = this.SendAndApplyAsync();
        return true;
    }

    /// <summary>Forgets the death (a new world entry starts alive).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _death = null;
            _pending = false;
        }
    }

    private async Task SendAndApplyAsync()
    {
        OpStatus status;
        try
        {
            status = await _send().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            status = OpStatus.Invalid;
        }

        lock (_gate)
        {
            _pending = false;
            if (status == OpStatus.Ok)
            {
                _death = null;
            }
        }
    }
}

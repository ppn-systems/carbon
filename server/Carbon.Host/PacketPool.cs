// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions;
using Nalix.Framework.Memory.Objects;

namespace Carbon.Host;

/// <summary>
/// Leases outbound packets from an <see cref="IObjectPoolManager"/> (ADR-043). <c>PacketFactory&lt;T&gt;.Acquire()</c>
/// allocates a new packet on every call in our setup (audit round 3, ALLOC-1), while the pool manager's
/// <c>Get</c>/<c>Return</c> allocates nothing. A lease is scoped around the awaited send
/// (<c>using PacketLease&lt;T&gt; lease = packets.Rent&lt;T&gt;(); … await send …</c>), so the packet goes back only after
/// the send completed or failed, never while the transport may still read it, and exactly once.
/// </summary>
/// <remarks>Thread-safe as far as the pool manager is: packets are rented on the tick thread and returned on send completion.</remarks>
internal sealed class PacketPool : IDisposable
{
    private readonly IObjectPoolManager _manager;
    private readonly IDisposable? _owned;

    /// <summary>Initializes a pool over its own <see cref="ObjectPoolManager"/>, disposed with the pool.</summary>
    public PacketPool()
    {
        ObjectPoolManager owned = new();
        _manager = owned;
        _owned = owned;
    }

    /// <summary>Initializes a pool over <paramref name="manager"/>, which the caller owns (tests pass a recording one).</summary>
    /// <param name="manager">The pool manager packets are rented from and returned to.</param>
    public PacketPool(IObjectPoolManager manager) =>
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));

    /// <summary>Disposes the pool manager this pool created, if any.</summary>
    public void Dispose() => _owned?.Dispose();

    /// <summary>Rents a packet; dispose the lease once the send has completed.</summary>
    /// <typeparam name="TPacket">The packet type.</typeparam>
    /// <returns>The lease.</returns>
    public PacketLease<TPacket> Rent<TPacket>()
        where TPacket : class, IPoolable, new() => new(this, _manager.Get<TPacket>());

    /// <summary>
    /// Clears <paramref name="packet"/> (<see cref="IPoolable.ResetForPool"/>: nothing of this send may reach the next
    /// recipient) and gives it back. Called once per lease, by its <see cref="PacketLease{TPacket}.Dispose"/>.
    /// </summary>
    internal void Return<TPacket>(TPacket packet)
        where TPacket : class, IPoolable, new()
    {
        packet.ResetForPool();
        _manager.Return(packet);
    }
}

/// <summary>A rented outbound packet (see <see cref="PacketPool"/>); disposing it returns the packet.</summary>
/// <typeparam name="TPacket">The packet type.</typeparam>
internal readonly struct PacketLease<TPacket> : IDisposable
    where TPacket : class, IPoolable, new()
{
    private readonly PacketPool _pool;

    internal PacketLease(PacketPool pool, TPacket value)
    {
        _pool = pool;
        this.Value = value;
    }

    /// <summary>Gets the rented packet. Do not keep it past the lease.</summary>
    public TPacket Value { get; }

    /// <summary>Returns the packet to the pool.</summary>
    public void Dispose() => _pool.Return(this.Value);
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Nalix.Codec.Pooling;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Repairing one item (ADR-028): sends <see cref="RepairItemRequest"/>, keeps the server's <see cref="RepairItemResult"/>
/// for the repair window, and shows a localized toast when the server refuses. One request is in flight at a time.
/// </summary>
/// <param name="tcp">Gets the TCP session while the world session is ready, else <see langword="null"/>.</param>
/// <param name="notify">Queues a HUD toast (thread-safe).</param>
public sealed class RepairSession(Func<TcpSession?> tcp, Action<string> notify) : IRepairSession
{
    private readonly Func<TcpSession?> _tcp = tcp ?? throw new ArgumentNullException(nameof(tcp));
    private readonly Action<string> _notify = notify ?? throw new ArgumentNullException(nameof(notify));
    private readonly Lock _gate = new();
    private RepairResultView _last;
    private uint _serial;
    private int _pending;

    /// <inheritdoc/>
    public bool IsPending => Volatile.Read(ref _pending) != 0;

    /// <inheritdoc/>
    public uint ResultSerial => Volatile.Read(ref _serial);

    /// <inheritdoc/>
    public RepairResultView LastResult
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    /// <summary>Gets the localized message key of a refusal (<c>[repair]</c> section).</summary>
    /// <param name="error">Server reason.</param>
    /// <param name="status">Queue or world status (used when the error is unset).</param>
    /// <returns>The key.</returns>
    public static string ErrorKey(RepairError error, OpStatus status) => error switch
    {
        RepairError.InvalidRequest => "repair.error.invalid",
        RepairError.EmptySlot or RepairError.ItemMismatch => "repair.error.item_moved",
        RepairError.NotRepairable => "repair.error.not_repairable",
        RepairError.NotDamaged => "repair.error.not_damaged",
        RepairError.TooManySteps => "repair.error.invalid",
        RepairError.OutOfRange => "repair.error.out_of_range",
        RepairError.StationTierTooLow => "repair.error.station_tier",
        RepairError.MissingMaterials => "repair.error.materials",
        RepairError.NotEnoughGold => "repair.error.gold",
        RepairError.None => status == OpStatus.OutOfRange ? "repair.error.out_of_range" : "repair.error.failed",
        _ => "repair.error.failed",
    };

    /// <inheritdoc/>
    public bool TryRepair(ushort slotIndex, ushort expectedItemId, byte steps, RepairVenue venue)
    {
        if (steps == 0 || venue == RepairVenue.None || _tcp() is not { IsConnected: true } session ||
            Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
        {
            return false;
        }

        _ = this.SendAsync(session, slotIndex, expectedItemId, steps, venue);
        return true;
    }

    private async Task SendAsync(TcpSession session, ushort slotIndex, ushort expectedItemId, byte steps, RepairVenue venue)
    {
        RepairResultView result;
        try
        {
            RequestOptions options = RequestOptions.Default.WithTimeout(4000).WithEncrypt(session.State.EncryptionEnabled);
            using PacketScope<RepairItemRequest> lease = PacketFactory<RepairItemRequest>.Acquire();
            lease.Value.SlotIndex = slotIndex;
            lease.Value.ExpectedItemId = expectedItemId;
            lease.Value.Steps = steps;
            lease.Value.Venue = venue;
            using RepairItemResult reply = await session.RequestAsync<RepairItemResult>(lease.Value, options, predicate: null, CancellationToken.None).ConfigureAwait(false);
            result = new RepairResultView(reply.Status, reply.Error, reply.SlotIndex, reply.Steps);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            result = new RepairResultView(OpStatus.Invalid, RepairError.None, slotIndex, 0);
        }

        lock (_gate)
        {
            _last = result;
        }

        _ = Interlocked.Increment(ref _serial);
        Volatile.Write(ref _pending, 0);
        if (!result.IsOk)
        {
            _notify(GameText.Get(ErrorKey(result.Error, result.Status)));
        }
    }
}

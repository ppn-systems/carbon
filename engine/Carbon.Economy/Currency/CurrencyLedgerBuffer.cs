// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Primitives.Buffers;

namespace Carbon.Economy.Currency;

/// <summary>
/// In-memory <see cref="ICurrencyLedger"/> keeping the most recent entries in a ring buffer (for draining to
/// persistence) and running faucet/sink totals per currency. Not thread-safe: owned by the world tick thread.
/// </summary>
public sealed class CurrencyLedgerBuffer : ICurrencyLedger
{
    private readonly RingBuffer<CurrencyLedgerEntry> _entries;
    private long _goldMinted;
    private long _goldBurned;
    private long _diamondsMinted;
    private long _diamondsBurned;

    /// <summary>
    /// Initializes a new instance of the <see cref="CurrencyLedgerBuffer"/> class.
    /// </summary>
    /// <param name="capacity">Number of recent entries retained (rounded up to a power of two).</param>
    public CurrencyLedgerBuffer(int capacity = 1024) => _entries = new RingBuffer<CurrencyLedgerEntry>(capacity);

    /// <summary>Gets the number of retained entries.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets the total number of entries ever recorded.</summary>
    public long TotalRecorded { get; private set; }

    /// <inheritdoc/>
    public void Record(in CurrencyLedgerEntry entry)
    {
        _entries.PushBack(entry);
        this.TotalRecorded++;

        // Trades move currency between players and are neither faucet nor sink.
        if (entry.Reason == CurrencyReason.Trade)
        {
            return;
        }

        if (entry.Currency == CurrencyKind.Gold)
        {
            if (entry.Delta >= 0)
            {
                _goldMinted = SaturatingAdd(_goldMinted, entry.Delta);
            }
            else
            {
                _goldBurned = SaturatingAdd(_goldBurned, -entry.Delta);
            }
        }
        else if (entry.Delta >= 0)
        {
            _diamondsMinted = SaturatingAdd(_diamondsMinted, entry.Delta);
        }
        else
        {
            _diamondsBurned = SaturatingAdd(_diamondsBurned, -entry.Delta);
        }
    }

    /// <summary>
    /// Gets the total amount created by faucets.
    /// </summary>
    /// <param name="currency">Currency.</param>
    /// <returns>Total credited, excluding trades.</returns>
    public long GetMinted(CurrencyKind currency) => currency == CurrencyKind.Gold ? _goldMinted : _diamondsMinted;

    /// <summary>
    /// Gets the total amount destroyed by sinks.
    /// </summary>
    /// <param name="currency">Currency.</param>
    /// <returns>Total debited, excluding trades.</returns>
    public long GetBurned(CurrencyKind currency) => currency == CurrencyKind.Gold ? _goldBurned : _diamondsBurned;

    /// <summary>
    /// Removes and returns the oldest retained entry.
    /// </summary>
    /// <param name="entry">The oldest entry.</param>
    /// <returns><see langword="true"/> when an entry was available.</returns>
    public bool TryDrain(out CurrencyLedgerEntry entry) => _entries.TryPopFront(out entry);

    private static long SaturatingAdd(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
}

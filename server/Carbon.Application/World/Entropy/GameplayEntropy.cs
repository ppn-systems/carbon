// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Primitives.Random;

namespace Carbon.Application.World.Entropy;

/// <summary>
/// Server-only source of gameplay randomness (ADR-026). Holds a 128-bit secret that is never derived from the public
/// world seed, never logged and never put in a packet, and hands out per-epoch subkeys:
/// <c>KeyAt(tick) = PRF(secret, epoch = ⌊tick / epochTicks⌋, bootSalt)</c>. Every forge, loot, crit, evasion, food
/// poisoning and spawn roll mixes that subkey with its own inputs (tick, ids, nonce), so knowing the world seed and the
/// tick predicts nothing, and a leaked subkey stops being useful when its epoch ends.
/// </summary>
/// <remarks>
/// The boot salt changes on every start (ticks restart from zero, so without it a restart would replay the previous
/// run's streams). Trader stock instead uses <see cref="TraderStockKey"/>, derived from the secret alone, so a persisted
/// secret keeps each restock epoch's stock stable across restarts. Pure and thread-safe: the same secret, salt and epoch
/// length always give the same keys, which keeps tests and replays deterministic.
/// </remarks>
public sealed class GameplayEntropy : IGameplayEntropy
{
    /// <summary>Default rotation period: ten minutes of server (tick) time.</summary>
    public const int DefaultEpochSeconds = 600;

    private readonly GameplayKey _secret;
    private readonly ulong _bootSalt;
    private readonly ulong _epochTicks;

    /// <summary>Initializes a new instance of the <see cref="GameplayEntropy"/> class.</summary>
    /// <param name="secret">The world's gameplay secret (generated, persisted or injected; never the world seed).</param>
    /// <param name="bootSalt">Per-process salt (random at boot; fixed only for tests and replays).</param>
    /// <param name="tickRate">Simulation tick rate in Hz.</param>
    /// <param name="epochSeconds">Rotation period in seconds of server time.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="secret"/> is the all-zero key.</exception>
    public GameplayEntropy(GameplayKey secret, ulong bootSalt, ushort tickRate, int epochSeconds = DefaultEpochSeconds)
    {
        if (secret.IsZero)
        {
            throw new ArgumentException("The gameplay secret must not be the all-zero key.", nameof(secret));
        }

        ArgumentOutOfRangeException.ThrowIfZero(tickRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(epochSeconds);
        _secret = secret;
        _bootSalt = bootSalt;
        _epochTicks = tickRate * (ulong)epochSeconds;
        this.TraderStockKey = secret.Derive(GameplayDomain.TraderStock, 0UL);
    }

    /// <summary>Gets the rotation period in ticks.</summary>
    public ulong EpochTicks => _epochTicks;

    /// <summary>
    /// Gets the trader stock key: derived from the secret only (no boot salt, no tick epoch), because trader stock already
    /// rotates per restock epoch and must not reshuffle when the server restarts.
    /// </summary>
    public GameplayKey TraderStockKey { get; }

    /// <summary>Creates a fresh, random 128-bit secret from the OS CSPRNG.</summary>
    /// <returns>The secret.</returns>
    public static GameplayKey GenerateSecret()
    {
        Span<byte> bytes = stackalloc byte[GameplayKey.SizeInBytes];
        do
        {
            RandomNumberGenerator.Fill(bytes);
        }
        while (bytes.IndexOfAnyExcept((byte)0) < 0);

        GameplayKey key = GameplayKey.FromBytes(bytes);
        CryptographicOperations.ZeroMemory(bytes);
        return key;
    }

    /// <summary>Creates a fresh, random 64-bit boot salt from the OS CSPRNG.</summary>
    /// <returns>The salt.</returns>
    public static ulong GenerateBootSalt()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    /// <summary>Creates an entropy source with a random secret and salt that lives only in this process.</summary>
    /// <param name="tickRate">Simulation tick rate in Hz.</param>
    /// <param name="epochSeconds">Rotation period in seconds of server time.</param>
    /// <returns>The entropy source.</returns>
    public static GameplayEntropy CreateEphemeral(ushort tickRate, int epochSeconds = DefaultEpochSeconds) =>
        new(GenerateSecret(), GenerateBootSalt(), tickRate, epochSeconds);

    /// <summary>
    /// Resolves the world's secret at boot: <paramref name="injectedSecret"/> when set (config or env, for tests and
    /// replays; the store is not touched), else the secret persisted in <paramref name="store"/>, which a fresh CSPRNG
    /// secret seeds on the first boot of a world.
    /// </summary>
    /// <param name="store">Persistent secret store.</param>
    /// <param name="injectedSecret">Secret from configuration, or <see langword="null"/>.</param>
    /// <param name="injectedBootSalt">Boot salt from configuration, or <see langword="null"/> for a random one.</param>
    /// <param name="tickRate">Simulation tick rate in Hz.</param>
    /// <param name="epochSeconds">Rotation period in seconds of server time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entropy source.</returns>
    public static async Task<GameplayEntropy> LoadAsync(
        IGameplaySecretStore store,
        GameplayKey? injectedSecret,
        ulong? injectedBootSalt,
        ushort tickRate,
        int epochSeconds = DefaultEpochSeconds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        GameplayKey secret;
        if (injectedSecret is { } injected)
        {
            secret = injected;
        }
        else
        {
            byte[] candidate = new byte[GameplayKey.SizeInBytes];
            GenerateSecret().WriteBytes(candidate);
            byte[] stored = await store.GetOrCreateAsync(candidate, ct).ConfigureAwait(false);
            try
            {
                secret = GameplayKey.FromBytes(stored);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(candidate);
                CryptographicOperations.ZeroMemory(stored);
            }
        }

        return new GameplayEntropy(secret, injectedBootSalt ?? GenerateBootSalt(), tickRate, epochSeconds);
    }

    /// <summary>Gets the rotation epoch that contains <paramref name="tick"/>.</summary>
    /// <param name="tick">Simulation tick.</param>
    /// <returns>The epoch.</returns>
    public ulong EpochAt(ulong tick) => tick / _epochTicks;

    /// <inheritdoc/>
    public GameplayKey KeyAt(ulong tick) => _secret.Derive(GameplayDomain.Epoch, tick / _epochTicks, _bootSalt);

    /// <summary>Returns a redacted description: the secret never appears in logs.</summary>
    /// <returns>A redacted string.</returns>
    public override string ToString() => $"GameplayEntropy(epochTicks={_epochTicks}, secret=redacted)";
}

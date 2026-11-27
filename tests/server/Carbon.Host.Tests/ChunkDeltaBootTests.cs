// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-039: booting on an existing developer database never fails on a stored chunk delta. A row in a retired format
/// (1 to 3) or a corrupt one is dropped with a warning, so that chunk comes back from its baseline; the other rows load.
/// </summary>
public sealed class ChunkDeltaBootTests
{
    private const ulong Seed = 1337UL;

    [Fact]
    public async Task InitializeFromStoreAsync_RetiredAndCorruptRows_AreDroppedAndBootCompletes()
    {
        byte[] current = Row(chunkX: 3);
        byte[] retired = Row(chunkX: 1);
        retired[0] = 3;
        byte[] corrupt = Row(chunkX: 2)[..20];
        IChunkDeltaRepository repo = Substitute.For<IChunkDeltaRepository>();
        repo.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Persisted(1, retired),
            Persisted(2, corrupt),
            Persisted(3, current),
        ]);
        CapturingLogger logger = new();
        AuthoritativeWorld world = new(
            Seed,
            32f,
            0.0008f,
            30,
            repo,
            Substitute.For<ICharacterStore>(),
            new AuthoritativeWorldMultiplayerTests.RecordingOutboundSink(),
            entropy: TestGameplayEntropy.Create(Seed),
            logger: logger);

        Func<Task> boot = () => world.InitializeFromStoreAsync();

        await boot.Should().NotThrowAsync("an old or corrupt row is dropped, never fatal");
        logger.Warnings.Should().HaveCount(2);
        logger.Warnings[0].Should().Contain("chunk (1, 0)").And.Contain("format 3 is retired");
        logger.Warnings[1].Should().Contain("chunk (2, 0)");
    }

    private static byte[] Row(int chunkX)
    {
        ChunkDelta delta = new(chunkX, 0);
        delta.SetPropState(1, 1, PropDestructibility.Create(50f) with { CurrentDurability = 0f, IsDestroyed = true }, respawnAtTick: 0UL);
        return delta.Serialize(currentTick: 0UL);
    }

    private static PersistedChunkDelta Persisted(int chunkX, byte[] payload) =>
        new(chunkX, 0, 1U, 0, payload, DateTimeOffset.UnixEpoch);

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                this.Warnings.Add(formatter(state, exception));
            }
        }
    }
}

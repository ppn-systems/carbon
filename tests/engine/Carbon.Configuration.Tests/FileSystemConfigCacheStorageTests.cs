// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Configuration.Caching;
using FluentAssertions;
using Xunit;

namespace Carbon.Configuration.Tests;

public sealed class FileSystemConfigCacheStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "carbon-config-cache-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void WriteThenRead_RoundTripsBytesAndDeleteRemovesThem()
    {
        FileSystemConfigCacheStorage storage = new(_directory);
        byte[] bytes = ConfigTestServer.Utf8("{\"a\":1}");

        storage.TryRead("items", out _).Should().BeFalse();
        storage.Write("items", bytes);
        storage.TryRead("items", out byte[]? read).Should().BeTrue();
        read.Should().Equal(bytes);
        File.Exists(Path.Combine(_directory, "items.json")).Should().BeTrue();

        storage.Delete("items");
        storage.TryRead("items", out _).Should().BeFalse();
    }

    [Fact]
    public void Write_ConcurrentWritersOfSameDocument_AllSucceedAndLeaveNoTempFiles()
    {
        FileSystemConfigCacheStorage storage = new(_directory);
        byte[] bytes = ConfigTestServer.Utf8("{\"monsters\":[1,2,3]}");

        System.Threading.Tasks.Parallel.For(0, 32, _ => storage.Write("monsters", bytes));

        storage.TryRead("monsters", out byte[]? read).Should().BeTrue();
        read.Should().Equal(bytes);
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Fact]
    public void Save_StorageFailure_ReturnsFalseInsteadOfThrowing()
    {
        ConfigDocumentStore store = new(new ThrowingStorage());

        store.Save("items", [1, 2]).Should().BeFalse();
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("Items")]
    [InlineData("a/b")]
    [InlineData("")]
    public void Write_InvalidName_Throws(string name)
    {
        FileSystemConfigCacheStorage storage = new(_directory);

        Action act = () => storage.Write(name, [1]);

        act.Should().Throw<ArgumentException>();
    }

    private sealed class ThrowingStorage : IConfigCacheStorage
    {
        public bool TryRead(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? utf8)
        {
            utf8 = null;
            return false;
        }

        public void Write(string name, ReadOnlySpan<byte> utf8) => throw new IOException("locked");

        public void Delete(string name)
        {
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

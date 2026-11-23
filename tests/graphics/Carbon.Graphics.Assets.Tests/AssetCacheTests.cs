// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Carbon.Graphics.Assets.Management;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Assets.Tests;

[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Testing asset cache ownership transfer and disposal")]
public sealed class AssetCacheTests
{
    private sealed class DisposableAsset : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => this.IsDisposed = true;
    }

    [Fact]
    public void Store_And_TryGet_NormalizesBackslashesAndSlashesIdentically()
    {
        // Arrange
        using AssetCache cache = new();
        DisposableAsset asset = new();

        // Act
        cache.Store("textures/hero.png", asset);
        bool found = cache.TryGet(@"textures\hero.png", out DisposableAsset? retrieved);

        // Assert
        found.Should().BeTrue();
        retrieved.Should().BeSameAs(asset);
    }

    [Fact]
    public void Unload_WhenAssetIsDisposable_DisposesTargetInstance()
    {
        // Arrange
        using AssetCache cache = new();
        DisposableAsset asset = new();
        cache.Store("sound/click.wav", asset);

        // Act
        bool removed = cache.Unload("sound/click.wav");

        // Assert
        removed.Should().BeTrue();
        asset.IsDisposed.Should().BeTrue();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void Dispose_CascadesDisposeAcrossAllCachedAssets()
    {
        // Arrange
        AssetCache cache = new();
        DisposableAsset asset1 = new();
        DisposableAsset asset2 = new();
        cache.Store("a", asset1);
        cache.Store("b", asset2);

        // Act
        cache.Dispose();

        // Assert
        asset1.IsDisposed.Should().BeTrue();
        asset2.IsDisposed.Should().BeTrue();
        cache.Count.Should().Be(0);
    }
}

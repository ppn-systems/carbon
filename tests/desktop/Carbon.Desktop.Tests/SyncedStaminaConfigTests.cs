// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Configuration;
using Carbon.Configuration.Catalog;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Survival.Stamina;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>ADR-033: the client predicts stamina only from the server's synced <c>stamina.json</c>.</summary>
public sealed class SyncedStaminaConfigTests
{
    [Fact]
    public void Apply_SyncedStaminaJson_ExposesTheServerTuningAndIgnoresInvalidDocuments()
    {
        SyncedStaminaConfig synced = new();
        byte[] shipped = File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "resources", "config", "stamina.json"));

        synced.Apply(Catalog(1, shipped)).Should().BeTrue();
        StaminaConfig? applied = synced.Current;
        applied.Should().NotBeNull();
        applied!.SprintDrainPerSecond.Should().Be(12f);

        synced.Apply(Catalog(2, "{\"sprintDrainPerSecond\": -1}"u8.ToArray())).Should().BeFalse();
        synced.Apply(new ConfigCatalog(3, [])).Should().BeFalse("a catalog without stamina.json keeps the last config");
        synced.Current!.SprintDrainPerSecond.Should().Be(12f);
    }

    private static ConfigCatalog Catalog(uint version, byte[] stamina) =>
        new(version, [new ConfigCatalogEntry(StaminaConfig.DocumentName, ConfigContentHash.Compute(stamina), version, stamina)]);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Carbon.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Carbon.slnx not found above the test output directory.");
    }
}

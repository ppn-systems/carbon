// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using Carbon.Contracts.Config;
using FluentAssertions;
using Xunit;

namespace Carbon.Server.Configuration.Tests;

/// <summary>Guards the shipped <c>resources/config/*.json</c> documents that every domain owns.</summary>
public sealed class ResourcesConfigTests
{
    private static readonly string s_configDirectory = Path.Combine(FindRepositoryRoot(), "resources", "config");

    [Fact]
    public void ResourcesConfig_EveryFile_HasValidUniqueName()
    {
        string[] names = [.. Directory.GetFiles(s_configDirectory, ConfigRepository.SearchPattern).Select(Path.GetFileNameWithoutExtension).Select(static n => n!)];

        names.Should().OnlyContain(static n => ConfigDocumentName.IsValid(n), "config files are named [a-z0-9_-]");
        names.Should().OnlyHaveUniqueItems(static n => n.ToUpperInvariant());
        names.Length.Should().BeLessThanOrEqualTo(ConfigProtocolLimits.MaxDocuments);
    }

    [Fact]
    public void ResourcesConfig_EveryFile_IsStrictJsonAndLoadsIntoRepository()
    {
        foreach (string file in Directory.GetFiles(s_configDirectory, ConfigRepository.SearchPattern))
        {
            Action parse = () => ConfigJsonCanonicalizer.Canonicalize(File.ReadAllBytes(file));
            parse.Should().NotThrow($"{Path.GetFileName(file)} must be strict JSON");
        }

        ConfigRepository repository = ConfigRepository.Load(s_configDirectory);
        repository.Entries.Should().OnlyContain(static e => e.ByteLength <= ConfigProtocolLimits.MaxDocumentBytes);
    }

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

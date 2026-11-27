// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Xunit;

namespace Carbon.Architecture.Tests;

public sealed class RepositoryLayoutTests
{
    [Fact]
    public void ParseLsFilesOutput_ConflictedPathListedPerIndexStage_ReturnsEachPathOnce()
    {
        // `git ls-files -z` during an unresolved merge: the conflicted file appears for stages 1, 2 and 3.
        const string output = "b/Clean.cs\0a/Conflicted.cs\0a/Conflicted.cs\0a/Conflicted.cs\0";

        RepositoryLayout.ParseLsFilesOutput(output).Should().Equal(
            ["a/Conflicted.cs", "b/Clean.cs"],
            "guards must read every tracked file once, or size caps see a conflicted class at about 3x its size");
    }
}

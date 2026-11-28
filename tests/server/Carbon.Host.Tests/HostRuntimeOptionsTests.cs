// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Abstractions.Configuration;
using Carbon.Infrastructure.Configuration;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>How the host reads its database settings from the environment (audit SEC-4).</summary>
public sealed class HostRuntimeOptionsTests
{
    [Fact]
    public void FromEnvironment_PostgresWithoutAConnectionString_FallsBackToBuiltInCredentials()
    {
        // Characterisation: a missing CARBON_DB_CONNECTION in Postgres mode silently uses credentials in the source.
        HostRuntimeOptions options = HostRuntimeOptions.FromEnvironment(Env(("CARBON_DB_PROVIDER", "postgres")));

        options.Provider.Should().Be(DatabaseProvider.Postgres);
        options.ConnectionString.Should().Be("Host=localhost;Port=5432;Database=carbon;Username=carbon;Password=carbon");
    }

    [Fact]
    public void FromEnvironment_NoProvider_UsesTheLocalSqliteFile()
    {
        HostRuntimeOptions options = HostRuntimeOptions.FromEnvironment(Env());

        options.Provider.Should().Be(DatabaseProvider.Sqlite);
        options.ConnectionString.Should().Be("Data Source=carbon_world.db");
    }

    private static Func<string, string?> Env(params (string Name, string Value)[] variables)
    {
        Dictionary<string, string> map = [];
        foreach ((string name, string value) in variables)
        {
            map[name] = value;
        }

        return name => map.GetValueOrDefault(name);
    }
}

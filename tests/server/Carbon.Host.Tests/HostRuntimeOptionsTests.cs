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
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromEnvironment_PostgresWithoutAConnectionString_FailsFastNamingTheVariable(string? connection)
    {
        // There are no built-in Postgres credentials: the boot fails with a message that says what to set.
        Func<string, string?> env = connection is null
            ? Env(("CARBON_DB_PROVIDER", "postgres"))
            : Env(("CARBON_DB_PROVIDER", "postgres"), ("CARBON_DB_CONNECTION", connection));

        Action read = () => HostRuntimeOptions.FromEnvironment(env);

        read.Should().Throw<InvalidOperationException>().WithMessage("*CARBON_DB_CONNECTION*no default*")
            .Which.Message.Should().NotContain("Password");
    }

    [Fact]
    public void FromEnvironment_PostgresWithAConnectionString_UsesIt()
    {
        const string connection = "Host=db;Port=5432;Database=carbon;Username=svc;Password=from-the-secret-store";
        HostRuntimeOptions options = HostRuntimeOptions.FromEnvironment(Env(("CARBON_DB_PROVIDER", "postgres"), ("CARBON_DB_CONNECTION", connection)));

        options.Provider.Should().Be(DatabaseProvider.Postgres);
        options.ConnectionString.Should().Be(connection);
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

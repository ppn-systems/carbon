// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>A server the player can pick on the Server screen (ADR-037).</summary>
/// <param name="Id">Stable id from <c>servers.ini</c>, or <see cref="ServerDirectory.CustomId"/> for a typed address.</param>
/// <param name="Name">Display name; empty for a custom server, which the UI names in the player's language.</param>
/// <param name="Host">Host name or address.</param>
/// <param name="Port">Port.</param>
public sealed record GameServer(string Id, string Name, string Host, ushort Port)
{
    /// <summary>Gets a value indicating whether this is a developer-typed address rather than a listed server.</summary>
    public bool IsCustom => this.Id == ServerDirectory.CustomId;

    /// <summary>Returns whether this server is at <paramref name="host"/>:<paramref name="port"/>.</summary>
    /// <param name="host">Host to compare (case-insensitive).</param>
    /// <param name="port">Port to compare.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    public bool IsAt(string host, ushort port) =>
        this.Port == port && string.Equals(this.Host, host, System.StringComparison.OrdinalIgnoreCase);
}

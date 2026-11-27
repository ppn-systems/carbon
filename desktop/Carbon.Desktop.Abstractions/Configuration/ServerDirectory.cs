// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// The servers the Server screen offers (ADR-037): the shipped <c>resources/client/servers.ini</c>, plus the endpoint the
/// client was started with when it is not listed (a developer server from <c>CARBON_SERVER_HOST</c> / <c>CARBON_TCP_PORT</c>).
/// </summary>
public sealed class ServerDirectory
{
    /// <summary>The id of a server that is not in <c>servers.ini</c>.</summary>
    public const string CustomId = "custom";

    /// <summary>File name of the shipped server list.</summary>
    public const string FileName = "servers.ini";

    private readonly List<GameServer> _servers;

    private ServerDirectory(List<GameServer> servers) => _servers = servers;

    /// <summary>Gets the servers in display order (never empty).</summary>
    public IReadOnlyList<GameServer> Servers => _servers;

    /// <summary>
    /// Builds the directory from INI text: one <c>[server.&lt;id&gt;]</c> section per server with <c>name</c>, <c>host</c>
    /// and <c>port</c>. The start endpoint is appended as a custom server when no listed server matches it.
    /// </summary>
    /// <param name="document">Parsed <c>servers.ini</c>, or <see langword="null"/> when the file is missing.</param>
    /// <param name="startHost">Endpoint host the client was started with.</param>
    /// <param name="startPort">Endpoint port the client was started with.</param>
    /// <returns>The directory.</returns>
    /// <exception cref="FormatException">A listed server lacks a name, host or valid port.</exception>
    public static ServerDirectory FromIni(IniDocument? document, string startHost, ushort startPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startHost);
        List<GameServer> servers = [];
        if (document is not null)
        {
            foreach (string section in document.Sections)
            {
                if (!section.StartsWith("server.", StringComparison.Ordinal))
                {
                    continue;
                }

                string id = section["server.".Length..];
                if (!document.TryGetValue(section + ".name", out string name) || name.Length == 0 ||
                    !document.TryGetValue(section + ".host", out string host) || host.Length == 0 ||
                    !document.TryGetValue(section + ".port", out string portText) ||
                    !ushort.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort port) || port == 0)
                {
                    throw new FormatException($"{FileName}: [{section}] needs name, host and a port 1-65535.");
                }

                servers.Add(new GameServer(id, name, host, port));
            }
        }

        if (!servers.Exists(s => s.IsAt(startHost, startPort)))
        {
            servers.Add(new GameServer(CustomId, string.Empty, startHost, startPort));
        }

        return new ServerDirectory(servers);
    }

    /// <summary>Loads the shipped list (searching up from the app and working directories) for <paramref name="options"/>.</summary>
    /// <param name="options">Client options holding the start endpoint.</param>
    /// <returns>The directory; only the start endpoint when the file is missing or malformed.</returns>
    public static ServerDirectory Load(DesktopOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string? path = TryFindFile();
        try
        {
            IniDocument? doc = path is null ? null : IniDocument.Load(path);
            return FromIni(doc, options.ServerHost, options.ServerTcpPort);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IniFormatException or FormatException)
        {
            Console.Error.WriteLine($"[Servers] Ignoring server list: {ex.Message}");
            return FromIni(null, options.ServerHost, options.ServerTcpPort);
        }
    }

    /// <summary>Returns the listed server at an endpoint, or <see langword="null"/>.</summary>
    /// <param name="host">Host.</param>
    /// <param name="port">Port.</param>
    /// <returns>The server, or <see langword="null"/>.</returns>
    public GameServer? Find(string host, ushort port) => _servers.Find(s => s.IsAt(host, port));

    private static string? TryFindFile()
    {
        string[] roots = [AppContext.BaseDirectory, Directory.GetCurrentDirectory()];
        for (int i = 0; i < roots.Length; i++)
        {
            for (DirectoryInfo? cursor = new(roots[i]); cursor is not null; cursor = cursor.Parent)
            {
                string candidate = Path.Combine(cursor.FullName, "resources", "client", FileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}

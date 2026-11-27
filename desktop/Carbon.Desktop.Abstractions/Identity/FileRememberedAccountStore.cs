// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Text;
using System.Globalization;
using System.IO;
using System.Text;
using Carbon.Desktop.Abstractions.Configuration;

namespace Carbon.Desktop.Abstractions.Identity;

/// <summary>
/// Stores the "Remember me" account as a small INI file in the player's own profile (ADR-037). The file holds the
/// username, the server and the server-issued token; it never holds a password. On Linux and macOS the file is created
/// with mode <c>0600</c> (owner read/write only); on Windows it inherits the per-user ACL of <c>%LOCALAPPDATA%</c>.
/// </summary>
public sealed class FileRememberedAccountStore : IRememberedAccountStore
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _path;

    /// <summary>Initializes a new instance of the <see cref="FileRememberedAccountStore"/> class.</summary>
    /// <param name="path">File path (see <see cref="DesktopOptions.AccountFile"/>).</param>
    public FileRememberedAccountStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>Gets the file path.</summary>
    public string FilePath => _path;

    /// <inheritdoc/>
    public RememberedAccount? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            IniDocument doc = IniDocument.Load(_path);
            if (!doc.TryGetValue("account.username", out string username) || username.Length == 0 ||
                !doc.TryGetValue("account.host", out string host) || host.Length == 0 ||
                !doc.TryGetValue("account.port", out string portText) ||
                !ushort.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort port) || port == 0 ||
                !doc.TryGetValue("account.token", out string tokenText) ||
                !doc.TryGetValue("account.expires", out string expiresText) ||
                !long.TryParse(expiresText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long expiresUnix))
            {
                return null;
            }

            byte[] token = Base64Url.DecodeFromChars(tokenText);
            return token.Length == 0
                ? null
                : new RememberedAccount(username, host, port, token, DateTimeOffset.FromUnixTimeSeconds(expiresUnix));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IniFormatException or FormatException or ArgumentOutOfRangeException)
        {
            Console.Error.WriteLine($"[Account] Ignoring unreadable remembered account: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public void Save(RememberedAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

        string text = string.Join(
            '\n',
            "; Carbon remembered sign-in (ADR-037). A revocable server token, never a password. Log out deletes this file.",
            "[account]",
            $"username = {IniDocument.Escape(account.Username)}",
            $"host = {IniDocument.Escape(account.ServerHost)}",
            $"port = {account.ServerPort.ToString(CultureInfo.InvariantCulture)}",
            $"token = {Base64Url.EncodeToString(account.Token)}",
            $"expires = {account.ExpiresAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            string.Empty);

        // Write a fresh owner-only file, then swap it in, so the token is never readable by others even for a moment.
        string temp = _path + ".tmp";
        FileStreamOptions create = new() { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            create.UnixCreateMode = OwnerOnly;
        }

        using (FileStream stream = new(temp, create))
        {
            byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
            stream.Write(bytes);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, OwnerOnly);
        }

        File.Move(temp, _path, overwrite: true);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }
}

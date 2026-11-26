// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Carbon.Contracts.Config;

namespace Carbon.Configuration.Caching;

/// <summary>
/// <see cref="IConfigCacheStorage"/> that keeps one <c>&lt;name&gt;.json</c> file per document in a directory.
/// Writes go to a temporary file first and are then moved into place, so a crash never leaves a half-written file
/// under the real name (and a corrupt file would be rejected by its hash anyway).
/// </summary>
public sealed class FileSystemConfigCacheStorage : IConfigCacheStorage
{
    private readonly string _directory;

    /// <summary>Initializes a new instance of the <see cref="FileSystemConfigCacheStorage"/> class.</summary>
    /// <param name="directory">Cache directory; created on first write.</param>
    public FileSystemConfigCacheStorage(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    /// <summary>Gets the absolute cache directory.</summary>
    public string Directory => _directory;

    /// <inheritdoc/>
    public bool TryRead(string name, [NotNullWhen(true)] out byte[]? utf8)
    {
        string path = this.PathOf(name);
        try
        {
            utf8 = File.ReadAllBytes(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            utf8 = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public void Write(string name, ReadOnlySpan<byte> utf8)
    {
        string path = this.PathOf(name);
        _ = System.IO.Directory.CreateDirectory(_directory);

        string temp = path + ".tmp";
        using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(utf8);
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <inheritdoc/>
    public void Delete(string name)
    {
        string path = this.PathOf(name);
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing cached yet.
        }
    }

    private string PathOf(string name)
    {
        // The name rule ([a-z0-9_-]) rules out separators and "..", so the path cannot leave the directory.
        if (!ConfigDocumentName.IsValid(name))
        {
            throw new ArgumentException($"'{name}' is not a valid config document name.", nameof(name));
        }

        return Path.Combine(_directory, name + ".json");
    }
}

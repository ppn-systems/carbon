// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;

namespace Carbon.Server.Configuration.Tests;

/// <summary>Disposable scratch config directory.</summary>
internal sealed class TempConfigDirectory : IDisposable
{
    public TempConfigDirectory()
    {
        this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "carbon-config-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(this.Path);
    }

    public string Path { get; }

    public void Write(string fileName, string content) => File.WriteAllText(System.IO.Path.Combine(this.Path, fileName), content);

    public void Delete(string fileName) => File.Delete(System.IO.Path.Combine(this.Path, fileName));

    public void Dispose() => Directory.Delete(this.Path, recursive: true);
}

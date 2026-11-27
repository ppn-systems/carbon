// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>An INI document failed to load; the message names the file and line (ADR-023 D1).</summary>
public sealed class IniFormatException : FormatException
{
    /// <summary>Initializes a new instance of the <see cref="IniFormatException"/> class.</summary>
    public IniFormatException()
        : this("<unknown>", 0, "invalid INI document")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IniFormatException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public IniFormatException(string message)
        : this("<unknown>", 0, message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IniFormatException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The cause.</param>
    public IniFormatException(string message, Exception innerException)
        : base(message, innerException) => this.SourceName = "<unknown>";

    /// <summary>Initializes a new instance of the <see cref="IniFormatException"/> class.</summary>
    /// <param name="sourceName">File name (or other source label).</param>
    /// <param name="line">1-based line number.</param>
    /// <param name="reason">What is wrong.</param>
    public IniFormatException(string sourceName, int line, string reason)
        : base($"{sourceName}:{line}: {reason}")
    {
        this.SourceName = sourceName;
        this.Line = line;
    }

    /// <summary>Gets the file name or source label.</summary>
    public string SourceName { get; }

    /// <summary>Gets the 1-based line number.</summary>
    public int Line { get; }
}

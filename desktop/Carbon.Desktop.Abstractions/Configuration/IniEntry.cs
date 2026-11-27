// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>One <c>key = value</c> line of an <see cref="IniDocument"/>.</summary>
/// <param name="Section">Section name (empty before the first section).</param>
/// <param name="Key">Key as written in its section.</param>
/// <param name="FullKey">Section-qualified key (<c>section.key</c>).</param>
/// <param name="Value">Unescaped value.</param>
/// <param name="Line">1-based line number of the key.</param>
public sealed record IniEntry(string Section, string Key, string FullKey, string Value, int Line);

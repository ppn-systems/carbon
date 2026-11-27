// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// Native-AOT-safe source-generated JSON metadata for locale files and the locale manifest (ADR-020 D3).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(LocaleManifest))]
public sealed partial class LocalizationJsonContext : JsonSerializerContext;

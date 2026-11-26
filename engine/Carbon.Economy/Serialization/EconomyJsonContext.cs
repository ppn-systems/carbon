// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Text.Json.Serialization;
using Carbon.Economy.Config;
using Carbon.Economy.Loot;

namespace Carbon.Economy.Serialization;

/// <summary>
/// Native-AOT source-generated JSON metadata for <c>economy.json</c> and <c>loot.json</c>.
/// Unknown properties are rejected so config typos fail loudly at load time.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EconomyConfig))]
[JsonSerializable(typeof(LootTableDocument))]
public sealed partial class EconomyJsonContext : JsonSerializerContext;

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Scaling;

namespace Carbon.Creatures.Serialization;

/// <summary>
/// Native-AOT-safe source-generated JSON metadata for creature configuration files. Enum values are read by name,
/// unknown members are rejected so typos in config fail fast.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(MonsterCatalogDocument))]
[JsonSerializable(typeof(SpawningConfig))]
internal sealed partial class CreaturesJsonContext : JsonSerializerContext;

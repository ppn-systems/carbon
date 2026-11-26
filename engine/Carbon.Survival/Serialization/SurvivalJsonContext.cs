// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Text.Json.Serialization;
using Carbon.Survival.Config;
using Carbon.Survival.Food;

namespace Carbon.Survival.Serialization;

/// <summary>
/// Source-generated (Native-AOT-safe) JSON metadata for <c>needs.json</c> and <c>food.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(NeedsConfig))]
[JsonSerializable(typeof(FoodDefinition[]))]
internal sealed partial class SurvivalJsonContext : JsonSerializerContext;

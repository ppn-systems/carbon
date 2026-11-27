// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Text.Json.Serialization;
using Carbon.Survival.Config;
using Carbon.Survival.Food;
using Carbon.Survival.Stamina;

namespace Carbon.Survival.Serialization;

/// <summary>
/// Source-generated (Native-AOT-safe) JSON metadata for <c>needs.json</c>, <c>food.json</c> and <c>stamina.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(NeedsConfig))]
[JsonSerializable(typeof(FoodDefinition[]))]
[JsonSerializable(typeof(StaminaConfig))]
internal sealed partial class SurvivalJsonContext : JsonSerializerContext;

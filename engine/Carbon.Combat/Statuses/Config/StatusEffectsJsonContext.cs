// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Text.Json.Serialization;

namespace Carbon.Combat.Statuses.Config;

/// <summary>Source-generated (Native-AOT-safe) JSON metadata for <c>status_effects.json</c>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(StatusEffectsDocument))]
internal sealed partial class StatusEffectsJsonContext : JsonSerializerContext;

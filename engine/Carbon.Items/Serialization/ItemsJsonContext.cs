// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Text.Json.Serialization;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Enhancement;

namespace Carbon.Items.Serialization;

/// <summary>
/// Native-AOT source-generated JSON metadata for item and recipe config documents.
/// Unknown properties are rejected so config typos fail loudly at load time.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ItemCatalogDocument))]
[JsonSerializable(typeof(RecipeCatalogDocument))]
[JsonSerializable(typeof(EnhancementDocument))]
public sealed partial class ItemsJsonContext : JsonSerializerContext;

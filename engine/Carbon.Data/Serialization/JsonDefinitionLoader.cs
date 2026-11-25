// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Carbon.Data.Abstractions;
using Carbon.Data.Registry;

namespace Carbon.Data.Serialization;

/// <summary>
/// Native-AOT-safe JSON loader for static definition registries using source-generated <see cref="JsonTypeInfo{T}"/> metadata.
/// </summary>
public static class JsonDefinitionLoader
{
    /// <summary>
    /// Deserializes a UTF-8 JSON array using <paramref name="jsonTypeInfo"/> and indexes it into a <see cref="DefinitionRegistry{TKey, TDef}"/>.
    /// </summary>
    /// <typeparam name="TKey">Definition key type.</typeparam>
    /// <typeparam name="TDef">Definition model type.</typeparam>
    /// <param name="utf8Json">UTF-8 encoded JSON array payload.</param>
    /// <param name="jsonTypeInfo">Source-generated JSON type metadata for <typeparamref name="TDef"/>[].</param>
    /// <param name="comparer">Optional key comparer.</param>
    /// <returns>Immutable <see cref="DefinitionRegistry{TKey, TDef}"/> populated from the JSON payload.</returns>
    public static DefinitionRegistry<TKey, TDef> LoadFromUtf8<TKey, TDef>(
        ReadOnlySpan<byte> utf8Json,
        JsonTypeInfo<TDef[]> jsonTypeInfo,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
        where TDef : IDataDefinition<TKey>
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        TDef[] items = JsonSerializer.Deserialize(utf8Json, jsonTypeInfo) ?? [];
        return new DefinitionRegistry<TKey, TDef>(items, comparer);
    }

    /// <summary>
    /// Deserializes a JSON string using <paramref name="jsonTypeInfo"/> and indexes it into a <see cref="DefinitionRegistry{TKey, TDef}"/>.
    /// </summary>
    /// <typeparam name="TKey">Definition key type.</typeparam>
    /// <typeparam name="TDef">Definition model type.</typeparam>
    /// <param name="json">JSON array string.</param>
    /// <param name="jsonTypeInfo">Source-generated JSON type metadata for <typeparamref name="TDef"/>[].</param>
    /// <param name="comparer">Optional key comparer.</param>
    /// <returns>Immutable <see cref="DefinitionRegistry{TKey, TDef}"/> populated from the JSON payload.</returns>
    public static DefinitionRegistry<TKey, TDef> LoadFromJson<TKey, TDef>(
        string json,
        JsonTypeInfo<TDef[]> jsonTypeInfo,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
        where TDef : IDataDefinition<TKey>
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        TDef[] items = JsonSerializer.Deserialize(json, jsonTypeInfo) ?? [];
        return new DefinitionRegistry<TKey, TDef>(items, comparer);
    }
}

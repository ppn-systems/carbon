// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Carbon.Data.Abstractions;

namespace Carbon.Data.Registry;

/// <summary>
/// Immutable O(1) lookup registry for static game definitions backed by <see cref="FrozenDictionary{TKey, TValue}"/>.
/// Compatible with Native AOT and trimming.
/// </summary>
/// <typeparam name="TKey">Definition identifier type.</typeparam>
/// <typeparam name="TDef">Definition model implementing <see cref="IDataDefinition{TKey}"/>.</typeparam>
public sealed class DefinitionRegistry<TKey, TDef>
    where TKey : notnull
    where TDef : IDataDefinition<TKey>
{
    private readonly TDef[] _items;
    private readonly FrozenDictionary<TKey, TDef> _byId;

    /// <summary>
    /// Gets the total number of registered definitions.
    /// </summary>
    public int Count => _items.Length;

    /// <summary>
    /// Gets a read-only span over all definitions in registration order.
    /// </summary>
    public ReadOnlySpan<TDef> All => _items;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefinitionRegistry{TKey, TDef}"/> class.
    /// </summary>
    /// <param name="definitions">Collection of definitions to index.</param>
    /// <param name="comparer">Optional equality comparer for <typeparamref name="TKey"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown when duplicate definition identifiers are supplied.</exception>
    public DefinitionRegistry(IEnumerable<TDef> definitions, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        List<TDef> list = [];
        Dictionary<TKey, TDef> map = new(comparer);

        foreach (TDef definition in definitions)
        {
            if (definition is null)
            {
                throw new ArgumentException("Definitions collection cannot contain null entries.", nameof(definitions));
            }

            if (!map.TryAdd(definition.Id, definition))
            {
                throw new InvalidOperationException($"Duplicate definition id '{definition.Id}' in {typeof(TDef).Name} registry.");
            }

            list.Add(definition);
        }

        _items = [.. list];
        _byId = map.ToFrozenDictionary(comparer);
    }

    /// <summary>
    /// Determines whether a definition with <paramref name="id"/> is registered.
    /// </summary>
    /// <param name="id">Definition identifier.</param>
    /// <returns><see langword="true"/> if present; otherwise, <see langword="false"/>.</returns>
    public bool Contains(TKey id) => _byId.ContainsKey(id);

    /// <summary>
    /// Attempts to look up a definition by <paramref name="id"/> in O(1) time.
    /// </summary>
    /// <param name="id">Definition identifier.</param>
    /// <param name="definition">The retrieved definition if found.</param>
    /// <returns><see langword="true"/> if found; otherwise, <see langword="false"/>.</returns>
    public bool TryGet(TKey id, [MaybeNullWhen(false)] out TDef definition) =>
        _byId.TryGetValue(id, out definition);

    /// <summary>
    /// Retrieves a required definition by <paramref name="id"/>, throwing <see cref="KeyNotFoundException"/> if absent.
    /// </summary>
    /// <param name="id">Definition identifier.</param>
    /// <returns>The matching definition.</returns>
    public TDef GetRequired(TKey id) =>
        _byId.TryGetValue(id, out TDef? definition)
            ? definition
            : throw new KeyNotFoundException($"Definition '{id}' was not found in {typeof(TDef).Name} registry.");
}

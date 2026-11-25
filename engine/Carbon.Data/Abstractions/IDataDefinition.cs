// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Data.Abstractions;

/// <summary>
/// Contract for immutable static data definitions indexed in a <see cref="Registry.DefinitionRegistry{TKey, TDef}"/>.
/// </summary>
/// <typeparam name="TKey">Key identifier type.</typeparam>
public interface IDataDefinition<out TKey> where TKey : notnull
{
    /// <summary>
    /// Gets the unique identifier of this definition.
    /// </summary>
    TKey Id { get; }
}

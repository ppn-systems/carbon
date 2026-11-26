// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Dependency-inverted repository port supplying declarative <c>.cvg.json</c> vector rigs and item artwork (DIP/OCP).
/// </summary>
public interface IVectorArtRepository
{
    /// <summary>Gets total loaded character appearance rigs.</summary>
    int CharacterCount { get; }

    /// <summary>Gets total loaded monster rigs.</summary>
    int MonsterCount { get; }

    /// <summary>Gets total loaded NPC rigs.</summary>
    int NpcCount { get; }

    /// <summary>Gets total loaded item vector definitions.</summary>
    int ItemCount { get; }

    /// <summary>Resolves a playable character rig by appearance preset (<c>0..3</c>).</summary>
    bool TryGetCharacter(byte preset, [NotNullWhen(true)] out CompiledVectorRig? rig);

    /// <summary>Resolves a monster rig by archetype ID (<c>101..104</c>).</summary>
    bool TryGetMonster(ushort archetypeId, [NotNullWhen(true)] out CompiledVectorRig? rig);

    /// <summary>Resolves an NPC rig by archetype ID (<c>201..203</c>).</summary>
    bool TryGetNpc(ushort archetypeId, [NotNullWhen(true)] out CompiledVectorRig? rig);

    /// <summary>Resolves an inventory item vector definition by item ID (<c>1..12</c>).</summary>
    bool TryGetItem(uint itemId, [NotNullWhen(true)] out CompiledItemVectorArt? item);
}

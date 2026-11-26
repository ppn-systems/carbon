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

    /// <summary>Gets total loaded world prop/tree blueprints.</summary>
    int PropCount { get; }

    /// <summary>Gets total loaded biome visual blueprints.</summary>
    int BiomeCount { get; }

    /// <summary>Gets total loaded structure blueprints.</summary>
    int StructureCount { get; }

    /// <summary>Resolves a playable character rig by appearance preset (<c>0..3</c>).</summary>
    bool TryGetCharacter(byte preset, [NotNullWhen(true)] out CompiledVectorRig? rig);

    /// <summary>Resolves a monster rig by archetype ID (<c>101..107</c>).</summary>
    bool TryGetMonster(ushort archetypeId, [NotNullWhen(true)] out CompiledVectorRig? rig);

    /// <summary>Resolves an NPC rig by archetype ID (<c>201..204</c>).</summary>
    bool TryGetNpc(ushort archetypeId, [NotNullWhen(true)] out CompiledVectorRig? rig);

    /// <summary>Resolves an inventory item vector definition by item ID (<c>1..32</c>).</summary>
    bool TryGetItem(uint itemId, [NotNullWhen(true)] out CompiledItemVectorArt? item);

    /// <summary>Resolves a HUD icon (e.g. <c>coin</c>, <c>diamond</c>) declared under <c>uiIcons</c> in <c>catalog.cvg.json</c>.</summary>
    bool TryGetUiIcon(string iconId, [NotNullWhen(true)] out CompiledItemVectorArt? icon);

    /// <summary>Resolves a compiled natural prop/tree definition by <c>MapPropType</c> (<c>1..22</c>).</summary>
    bool TryGetProp(byte propType, [NotNullWhen(true)] out CompiledPropArt? prop);

    /// <summary>Resolves a compiled biome visual blueprint by <c>BiomeType</c> (<c>0..13</c>).</summary>
    bool TryGetBiome(byte biomeId, [NotNullWhen(true)] out CompiledBiomeArt? biome);

    /// <summary>Resolves a compiled structure blueprint by <c>StructureType</c> (<c>1..8</c>).</summary>
    bool TryGetStructure(byte structureType, [NotNullWhen(true)] out CompiledStructureArt? structureArt);

    /// <summary>Gets the human-readable localized/standardized display name for <paramref name="biomeId"/>.</summary>
    string GetBiomeDisplayName(byte biomeId);
}

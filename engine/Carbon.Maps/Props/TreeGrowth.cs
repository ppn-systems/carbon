// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Props;

/// <summary>
/// The growth of a tree recorded in a chunk delta (ADR-040 D1): its stage and its type. The type is kept so a tree can grow
/// while its chunk is not loaded. <c>default</c> is a mature tree, or any prop that is not a tree.
/// </summary>
/// <param name="Stage">The growth stage.</param>
/// <param name="Type">The tree type it grows into.</param>
public readonly record struct TreeGrowth(TreeGrowthStage Stage, MapPropType Type);

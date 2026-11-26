// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Runtime.CompilerServices;

namespace Carbon.Survival.Components;

/// <summary>
/// Fixed-size inline storage for <see cref="MoodMemory"/> so <see cref="NeedsComponent"/> stays unmanaged.
/// </summary>
[InlineArray(NeedsComponent.MoodMemoryCapacity)]
public struct MoodMemoryBuffer
{
    private MoodMemory _element0;
}

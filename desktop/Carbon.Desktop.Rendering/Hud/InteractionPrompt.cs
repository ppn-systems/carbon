// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Desktop.Abstractions.World;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>The interact-key prompt to draw this frame: what the key does and where its target is on screen.</summary>
/// <param name="Kind">What the key does to the target.</param>
/// <param name="ScreenAnchor">Screen point just above the target; the prompt is centred on it and drawn above it.</param>
public readonly record struct InteractionPrompt(InteractionKind Kind, Vector2 ScreenAnchor);

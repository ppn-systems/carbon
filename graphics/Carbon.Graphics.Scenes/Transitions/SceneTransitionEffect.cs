// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.Scenes.Transitions;

/// <summary>
/// Specifies the visual effect style executed during scene switching.
/// </summary>
public enum SceneTransitionEffect
{
    /// <summary>
    /// Immediate cut without visual transition effects.
    /// </summary>
    None = 0,

    /// <summary>
    /// Smooth full-screen color fade out to black/color and fade in to new scene.
    /// </summary>
    Fade = 1,

    /// <summary>
    /// High-intensity flash to white followed by fade-in.
    /// </summary>
    Flash = 2
}

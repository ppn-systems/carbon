// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.UI.Common;

/// <summary>
/// Specifies the user interaction state of an interactive UI control.
/// </summary>
public enum ControlState
{
    /// <summary>
    /// The control is idle and not interacted with.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// The mouse cursor is hovering over the control.
    /// </summary>
    Hovered = 1,

    /// <summary>
    /// The control is actively pressed down by mouse or touch.
    /// </summary>
    Pressed = 2,

    /// <summary>
    /// The control is disabled and does not accept user interactions.
    /// </summary>
    Disabled = 3
}

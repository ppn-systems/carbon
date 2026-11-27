// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Hit-test result for the inventory split/confirm dialog.</summary>
public enum InventoryDialogButton : byte
{
    /// <summary>Outside the dialog.</summary>
    None = 0,

    /// <summary>Inside the dialog, on no button.</summary>
    Body = 1,

    /// <summary>Split / Drop / Destroy.</summary>
    Confirm = 2,

    /// <summary>Cancel.</summary>
    Cancel = 3,

    /// <summary>One fewer unit (split).</summary>
    Minus = 4,

    /// <summary>One more unit (split).</summary>
    Plus = 5,
}

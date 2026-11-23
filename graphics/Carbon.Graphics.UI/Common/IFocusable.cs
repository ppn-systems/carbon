// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.UI.Common;

/// <summary>
/// Defines the contract for user interface controls capable of receiving, holding, and releasing keyboard input focus.
/// </summary>
public interface IFocusable
{
    /// <summary>
    /// Gets or sets a value indicating whether this control currently possesses active keyboard focus.
    /// </summary>
    bool IsFocused { get; set; }

    /// <summary>
    /// Gets a value indicating whether this control is currently eligible to receive focus.
    /// </summary>
    bool IsFocusable { get; }

    /// <summary>
    /// Gets or sets the sequential tab order navigation index.
    /// </summary>
    int TabIndex { get; set; }

    /// <summary>
    /// Called when keyboard focus is acquired by this control.
    /// </summary>
    void OnFocusGained();

    /// <summary>
    /// Called when keyboard focus is relinquished by this control.
    /// </summary>
    void OnFocusLost();
}

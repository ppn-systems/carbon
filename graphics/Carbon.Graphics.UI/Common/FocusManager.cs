// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Carbon.Graphics.UI.Common;

/// <summary>
/// Manages keyboard focus cycling, tab navigation, and focus transitions across interactive UI controls.
/// Zero heap allocation on hot path focus querying and updating.
/// </summary>
public sealed class FocusManager
{
    private readonly List<IFocusable> _controls = [];
    private IFocusable? _focusedControl;

    /// <summary>
    /// Occurs when keyboard focus shifts from one control to another.
    /// Parameters are the previously focused control (or null) and the newly focused control (or null).
    /// </summary>
    public event Action<IFocusable?, IFocusable?>? FocusChanged;

    /// <summary>
    /// Gets the control that currently holds active keyboard focus, or null if no control is focused.
    /// </summary>
    public IFocusable? FocusedControl => _focusedControl;

    /// <summary>
    /// Registers a focusable control into the tab navigation roster.
    /// </summary>
    /// <param name="control">The control to register.</param>
    public void Register(IFocusable control)
    {
        ArgumentNullException.ThrowIfNull(control, nameof(control));
        if (!_controls.Contains(control))
        {
            _controls.Add(control);
        }
    }

    /// <summary>
    /// Unregisters a focusable control from the tab navigation roster.
    /// </summary>
    /// <param name="control">The control to remove.</param>
    public void Unregister(IFocusable control)
    {
        ArgumentNullException.ThrowIfNull(control, nameof(control));
        if (_focusedControl == control)
        {
            this.ClearFocus();
        }

        _ = _controls.Remove(control);
    }

    /// <summary>
    /// Removes all controls from the focus roster and clears active focus.
    /// </summary>
    public void Clear()
    {
        this.ClearFocus();
        _controls.Clear();
    }

    /// <summary>
    /// Explicitly assigns keyboard focus to the specified control.
    /// </summary>
    /// <param name="control">The control to focus, or null to clear focus.</param>
    public void Focus(IFocusable? control)
    {
        if (ReferenceEquals(_focusedControl, control))
        {
            return;
        }

        if (control != null && (!control.IsFocusable || !_controls.Contains(control)))
        {
            return;
        }

        IFocusable? previous = _focusedControl;
        if (previous != null)
        {
            previous.IsFocused = false;
            previous.OnFocusLost();
        }

        _focusedControl = control;

        if (control != null)
        {
            control.IsFocused = true;
            control.OnFocusGained();
        }

        this.FocusChanged?.Invoke(previous, control);
    }

    /// <summary>
    /// Relinquishes active keyboard focus.
    /// </summary>
    public void ClearFocus() =>
        this.Focus(null);

    /// <summary>
    /// Advances keyboard focus to the next eligible control in sequential tab order.
    /// </summary>
    public void Next() =>
        this.Navigate(forward: true);

    /// <summary>
    /// Reverses keyboard focus to the preceding eligible control in sequential tab order.
    /// </summary>
    public void Previous() =>
        this.Navigate(forward: false);

    /// <summary>
    /// Handles keyboard tab events to advance or reverse focus.
    /// </summary>
    /// <param name="tabPressed">Whether the Tab key was pressed this frame.</param>
    /// <param name="shiftHeld">Whether the Shift modifier key is currently held down.</param>
    public void HandleTabInput(bool tabPressed, bool shiftHeld = false)
    {
        if (!tabPressed || _controls.Count == 0)
        {
            return;
        }

        if (shiftHeld)
        {
            this.Previous();
        }
        else
        {
            this.Next();
        }
    }

    private void Navigate(bool forward)
    {
        if (_controls.Count == 0)
        {
            return;
        }

        // Sort controls in-place by TabIndex
        _controls.Sort(static (a, b) => a.TabIndex.CompareTo(b.TabIndex));

        int currentIndex = _focusedControl != null ? _controls.IndexOf(_focusedControl) : -1;
        int count = _controls.Count;

        for (int step = 1; step <= count; step++)
        {
            int nextIndex = forward
                ? (currentIndex + step) % count
                : (currentIndex - step + count) % count;

            IFocusable candidate = _controls[nextIndex];
            if (candidate.IsFocusable)
            {
                this.Focus(candidate);
                return;
            }
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Windowing.Abstractions;
using Silk.NET.Input;

namespace Carbon.Graphics.Windowing.Input;

/// <summary>
/// Manages and listens to hardware input devices from Silk.NET, buffering states for zero-allocation tick querying.
/// </summary>
public sealed class InputManager : IInputManager, IDisposable
{
    private const int MaxKeys = 512;
    private const int MaxMouseButtons = 16;

    private readonly Silk.NET.Input.IInputContext _silkInput;
    private readonly IKeyboard? _keyboard;
    private readonly IMouse? _mouse;

    private readonly bool[] _liveKeys = new bool[MaxKeys];
    private readonly bool[] _pressedKeyLatch = new bool[MaxKeys];
    private readonly bool[] _currentKeys = new bool[MaxKeys];
    private readonly bool[] _previousKeys = new bool[MaxKeys];

    private readonly bool[] _liveButtons = new bool[MaxMouseButtons];
    private readonly bool[] _pressedButtonLatch = new bool[MaxMouseButtons];
    private readonly bool[] _currentButtons = new bool[MaxMouseButtons];
    private readonly bool[] _previousButtons = new bool[MaxMouseButtons];

    private Vector2 _liveMousePos;
    private Vector2 _currentMousePos;
    private Vector2 _previousMousePos;
    private Vector2 _accumulatedScroll;

    private KeyboardSnapshot _keyboardSnapshot;
    private MouseSnapshot _mouseSnapshot;

    /// <inheritdoc/>
    public KeyboardSnapshot Keyboard => _keyboardSnapshot;

    /// <inheritdoc/>
    public MouseSnapshot Mouse => _mouseSnapshot;

    /// <inheritdoc/>
    public event Action<char>? CharacterReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="InputManager"/> class wrapping a Silk.NET input context.
    /// </summary>
    /// <param name="silkInput">The Silk.NET input context.</param>
    public InputManager(Silk.NET.Input.IInputContext silkInput)
    {
        _silkInput = silkInput ?? throw new ArgumentNullException(nameof(silkInput));

        if (_silkInput.Keyboards.Count > 0)
        {
            _keyboard = _silkInput.Keyboards[0];
            _keyboard.KeyDown += this.OnKeyDown;
            _keyboard.KeyUp += this.OnKeyUp;
            _keyboard.KeyChar += this.OnKeyChar;
        }

        if (_silkInput.Mice.Count > 0)
        {
            _mouse = _silkInput.Mice[0];
            _mouse.MouseDown += this.OnMouseDown;
            _mouse.MouseUp += this.OnMouseUp;
            _mouse.MouseMove += this.OnMouseMove;
            _mouse.Scroll += this.OnMouseScroll;
            _liveMousePos = new Vector2(_mouse.Position.X, _mouse.Position.Y);
            _currentMousePos = _liveMousePos;
            _previousMousePos = _currentMousePos;
        }

        _keyboardSnapshot = new KeyboardSnapshot(_currentKeys, _previousKeys);
        _mouseSnapshot = new MouseSnapshot(_currentMousePos, _previousMousePos, Vector2.Zero, _currentButtons, _previousButtons);
    }

    /// <summary>
    /// Advances input state buffers at the beginning of each frame tick.
    /// Zero heap allocation.
    /// </summary>
    public void Update()
    {
        // Shift the previous frame's snapshot state before latching incoming events
        Array.Copy(_currentKeys, _previousKeys, MaxKeys);
        Array.Copy(_currentButtons, _previousButtons, MaxMouseButtons);
        _previousMousePos = _currentMousePos;

        for (int i = 0; i < MaxKeys; i++)
        {
            _currentKeys[i] = _liveKeys[i] || _pressedKeyLatch[i];
            _pressedKeyLatch[i] = false;
        }

        for (int i = 0; i < MaxMouseButtons; i++)
        {
            _currentButtons[i] = _liveButtons[i] || _pressedButtonLatch[i];
            _pressedButtonLatch[i] = false;
        }

        _currentMousePos = _liveMousePos;

        Vector2 scrollDelta = _accumulatedScroll;
        _accumulatedScroll = Vector2.Zero;

        _keyboardSnapshot = new KeyboardSnapshot(_currentKeys, _previousKeys);
        _mouseSnapshot = new MouseSnapshot(_currentMousePos, _previousMousePos, scrollDelta, _currentButtons, _previousButtons);
    }

    /// <inheritdoc/>
    public bool IsKeyDown(Key key) => _keyboardSnapshot.IsKeyDown(key);

    /// <inheritdoc/>
    public bool IsKeyPressed(Key key) => _keyboardSnapshot.IsKeyPressed(key);

    /// <inheritdoc/>
    public bool IsKeyReleased(Key key) => _keyboardSnapshot.IsKeyReleased(key);

    /// <inheritdoc/>
    public bool IsMouseButtonDown(MouseButton button) => _mouseSnapshot.IsButtonDown(button);

    /// <inheritdoc/>
    public bool IsMouseButtonPressed(MouseButton button) => _mouseSnapshot.IsButtonPressed(button);

    /// <inheritdoc/>
    public bool IsMouseButtonReleased(MouseButton button) => _mouseSnapshot.IsButtonReleased(button);

    private void OnKeyDown(IKeyboard keyboard, Key key, int keyCode)
    {
        int idx = (int)key;
        if (idx >= 0 && idx < MaxKeys)
        {
            _liveKeys[idx] = true;
            _pressedKeyLatch[idx] = true;
        }
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int keyCode)
    {
        int idx = (int)key;
        if (idx >= 0 && idx < MaxKeys)
        {
            _liveKeys[idx] = false;
        }
    }

    private void OnKeyChar(IKeyboard keyboard, char character) =>
        this.CharacterReceived?.Invoke(character);

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        int idx = (int)button;
        if (idx >= 0 && idx < MaxMouseButtons)
        {
            _liveButtons[idx] = true;
            _pressedButtonLatch[idx] = true;
        }
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        int idx = (int)button;
        if (idx >= 0 && idx < MaxMouseButtons)
        {
            _liveButtons[idx] = false;
        }
    }

    private void OnMouseMove(IMouse mouse, Vector2 position) =>
        _liveMousePos = position;

    private void OnMouseScroll(IMouse mouse, ScrollWheel wheel) =>
        _accumulatedScroll += new Vector2(wheel.X, wheel.Y);

    /// <summary>
    /// Releases subscribed event listeners.
    /// </summary>
    public void Dispose()
    {
        if (_keyboard != null)
        {
            _keyboard.KeyDown -= this.OnKeyDown;
            _keyboard.KeyUp -= this.OnKeyUp;
            _keyboard.KeyChar -= this.OnKeyChar;
        }

        if (_mouse != null)
        {
            _mouse.MouseDown -= this.OnMouseDown;
            _mouse.MouseUp -= this.OnMouseUp;
            _mouse.MouseMove -= this.OnMouseMove;
            _mouse.Scroll -= this.OnMouseScroll;
        }

        _silkInput.Dispose();
    }
}

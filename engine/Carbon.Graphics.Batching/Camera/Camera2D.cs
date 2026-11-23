// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Graphics.Batching.Camera;

/// <summary>
/// Provides a 2D/2.5D orthographic camera with zoom, rotation, coordinate transformations, and frustum bounds.
/// </summary>
public sealed class Camera2D
{
    private Vector2 _position;
    private float _zoom = 1.0f;
    private float _rotation;
    private Vector2 _viewportSize;
    private Vector2 _origin;
    private bool _isDirty = true;

    private Matrix4x4 _viewMatrix = Matrix4x4.Identity;
    private Matrix4x4 _projectionMatrix = Matrix4x4.Identity;
    private Matrix4x4 _viewProjectionMatrix = Matrix4x4.Identity;
    private Matrix4x4 _inverseViewProjectionMatrix = Matrix4x4.Identity;

    /// <summary>
    /// Gets or sets the 2D world position of the camera center.
    /// </summary>
    public Vector2 Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                _position = value;
                _isDirty = true;
            }
        }
    }

    /// <summary>
    /// Gets or sets the camera zoom factor (1.0 = normal, 2.0 = 2x magnification). Must be greater than 0.
    /// </summary>
    public float Zoom
    {
        get => _zoom;
        set
        {
            float clamped = MathF.Max(0.0001f, value);
            if (_zoom != clamped)
            {
                _zoom = clamped;
                _isDirty = true;
            }
        }
    }

    /// <summary>
    /// Gets or sets the camera rotation angle in radians.
    /// </summary>
    public float Rotation
    {
        get => _rotation;
        set
        {
            if (_rotation != value)
            {
                _rotation = value;
                _isDirty = true;
            }
        }
    }

    /// <summary>
    /// Gets or sets the viewport dimensions in screen pixels.
    /// </summary>
    public Vector2 ViewportSize
    {
        get => _viewportSize;
        set
        {
            if (_viewportSize != value)
            {
                _viewportSize = value;
                _origin = value * 0.5f;
                _isDirty = true;
            }
        }
    }

    /// <summary>
    /// Gets the camera view matrix.
    /// </summary>
    public Matrix4x4 ViewMatrix
    {
        get
        {
            this.EnsureMatricesUpdated();
            return _viewMatrix;
        }
    }

    /// <summary>
    /// Gets the orthographic projection matrix.
    /// </summary>
    public Matrix4x4 ProjectionMatrix
    {
        get
        {
            this.EnsureMatricesUpdated();
            return _projectionMatrix;
        }
    }

    /// <summary>
    /// Gets the combined view-projection matrix ready to be set as a shader uniform.
    /// </summary>
    public Matrix4x4 ViewProjectionMatrix
    {
        get
        {
            this.EnsureMatricesUpdated();
            return _viewProjectionMatrix;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Camera2D"/> class with initial viewport dimensions.
    /// </summary>
    /// <param name="viewportWidth">The width of the viewport in pixels.</param>
    /// <param name="viewportHeight">The height of the viewport in pixels.</param>
    public Camera2D(float viewportWidth, float viewportHeight) => this.ViewportSize = new Vector2(viewportWidth, viewportHeight);

    /// <summary>
    /// Transforms a screen coordinate (e.g. mouse cursor position) into world space coordinates.
    /// </summary>
    /// <param name="screenPosition">The screen pixel coordinate.</param>
    /// <returns>The corresponding world coordinate.</returns>
    public Vector2 ScreenToWorld(in Vector2 screenPosition)
    {
        this.EnsureMatricesUpdated();

        // Convert screen coords [0, ViewportSize] to normalized device coordinates [-1, 1]
        float ndcX = (screenPosition.X / _viewportSize.X * 2.0f) - 1.0f;
        float ndcY = 1.0f - (screenPosition.Y / _viewportSize.Y * 2.0f);

        Vector4 ndc = new(ndcX, ndcY, 0f, 1.0f);
        Vector4 world = Vector4.Transform(ndc, _inverseViewProjectionMatrix);

        return world.W != 0.0f
            ? new Vector2(world.X / world.W, world.Y / world.W)
            : new Vector2(world.X, world.Y);
    }

    /// <summary>
    /// Transforms a world space coordinate into screen pixel coordinates.
    /// </summary>
    /// <param name="worldPosition">The world coordinate.</param>
    /// <returns>The screen pixel coordinate.</returns>
    public Vector2 WorldToScreen(in Vector2 worldPosition)
    {
        this.EnsureMatricesUpdated();

        Vector4 world = new(worldPosition.X, worldPosition.Y, 0f, 1.0f);
        Vector4 clip = Vector4.Transform(world, _viewProjectionMatrix);

        if (clip.W != 0.0f)
        {
            clip.X /= clip.W;
            clip.Y /= clip.W;
        }

        float screenX = (clip.X + 1.0f) * 0.5f * _viewportSize.X;
        float screenY = (1.0f - clip.Y) * 0.5f * _viewportSize.Y;

        return new Vector2(screenX, screenY);
    }

    /// <summary>
    /// Computes the axis-aligned bounding box of the visible world area for frustum culling.
    /// </summary>
    /// <returns>A <see cref="RectangleF"/> bounding the visible world region.</returns>
    public RectangleF GetVisibleBounds()
    {
        if (_rotation == 0f)
        {
            float invZoom = 1.0f / _zoom;
            float worldW = _viewportSize.X * invZoom;
            float worldH = _viewportSize.Y * invZoom;
            float halfW = worldW * 0.5f;
            float halfH = worldH * 0.5f;
            return new RectangleF(_position.X - halfW, _position.Y - halfH, worldW, worldH);
        }

        Vector2 topLeft = this.ScreenToWorld(Vector2.Zero);
        Vector2 topRight = this.ScreenToWorld(new Vector2(_viewportSize.X, 0f));
        Vector2 bottomLeft = this.ScreenToWorld(new Vector2(0f, _viewportSize.Y));
        Vector2 bottomRight = this.ScreenToWorld(_viewportSize);

        float minX = MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomLeft.X, bottomRight.X));
        float maxX = MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomLeft.X, bottomRight.X));
        float minY = MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomLeft.Y, bottomRight.Y));
        float maxY = MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomLeft.Y, bottomRight.Y));

        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Smoothly interpolates the camera center position toward a target position using framerate-independent exponential smoothing.
    /// </summary>
    /// <param name="target">The target world coordinates.</param>
    /// <param name="smoothing">The smoothing speed factor (higher values catch up faster).</param>
    /// <param name="dt">Elapsed delta time in seconds.</param>
    public void LerpTo(Vector2 target, float smoothing, double dt)
    {
        float t = 1.0f - MathF.Exp(-smoothing * (float)dt);
        this.Position = Vector2.Lerp(this.Position, target, t);
    }

    /// <summary>
    /// Constrains the visible camera viewport so it never displays coordinates outside the specified world bounds rectangle.
    /// </summary>
    /// <param name="worldBounds">The boundary rectangle bounding the playable world area.</param>
    public void ClampToBounds(in RectangleF worldBounds)
    {
        RectangleF visible = this.GetVisibleBounds();
        float halfW = visible.Width * 0.5f;
        float halfH = visible.Height * 0.5f;

        float minX = worldBounds.Left + halfW;
        float maxX = worldBounds.Right - halfW;
        float minY = worldBounds.Top + halfH;
        float maxY = worldBounds.Bottom - halfH;

        float clampedX = minX <= maxX ? Math.Clamp(this.Position.X, minX, maxX) : (worldBounds.Left + worldBounds.Right) * 0.5f;
        float clampedY = minY <= maxY ? Math.Clamp(this.Position.Y, minY, maxY) : (worldBounds.Top + worldBounds.Bottom) * 0.5f;

        this.Position = new Vector2(clampedX, clampedY);
    }

    private void EnsureMatricesUpdated()
    {
        if (!_isDirty)
        {
            return;
        }

        Matrix4x4 translationToOrigin = Matrix4x4.CreateTranslation(-_position.X, -_position.Y, 0f);
        Matrix4x4 rotation = Matrix4x4.CreateRotationZ(_rotation);
        Matrix4x4 scale = Matrix4x4.CreateScale(_zoom, _zoom, 1f);
        Matrix4x4 translationBack = Matrix4x4.CreateTranslation(_origin.X, _origin.Y, 0f);

        _viewMatrix = translationToOrigin * rotation * scale * translationBack;
        _projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(0f, _viewportSize.X, _viewportSize.Y, 0f, -1000f, 1000f);

        _viewProjectionMatrix = _viewMatrix * _projectionMatrix;

        if (Matrix4x4.Invert(_viewProjectionMatrix, out Matrix4x4 inverse))
        {
            _inverseViewProjectionMatrix = inverse;
        }

        _isDirty = false;
    }
}

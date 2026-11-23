namespace Carbon.Graphics.OpenGL;

/// <summary>
/// Defines low-level graphics device capabilities and context management.
/// </summary>
public interface IGraphicsDevice
{
    /// <summary>
    /// Gets a value indicating whether the graphics context is initialized and active.
    /// </summary>
    bool IsActive { get; }
}

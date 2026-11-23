namespace Carbon.Graphics.OpenGL.Buffers;

/// <summary>
/// Specifies the expected usage pattern of a GPU buffer.
/// </summary>
public enum BufferUsage
{
    /// <summary>
    /// The buffer data is modified once and used many times for drawing (e.g. static terrain, static meshes).
    /// </summary>
    StaticDraw = 0,

    /// <summary>
    /// The buffer data is modified repeatedly and used many times for drawing (e.g. dynamic sprite batches).
    /// </summary>
    DynamicDraw = 1,

    /// <summary>
    /// The buffer data is modified once and used at most a few times (e.g. per-frame streaming geometry).
    /// </summary>
    StreamDraw = 2
}

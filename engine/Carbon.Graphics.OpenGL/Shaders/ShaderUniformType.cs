// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Carbon.Graphics.OpenGL.Shaders;

/// <summary>
/// Specifies the data type of an active shader uniform.
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifiers should not contain type names", Justification = "Represents hardware shader uniform types.")]
public enum ShaderUniformType
{
    /// <summary>
    /// 32-bit signed integer or sampler texture unit.
    /// </summary>
    Int = 0,

    /// <summary>
    /// 32-bit floating point.
    /// </summary>
    Float = 1,

    /// <summary>
    /// 2-component floating point vector.
    /// </summary>
    Vector2 = 2,

    /// <summary>
    /// 3-component floating point vector.
    /// </summary>
    Vector3 = 3,

    /// <summary>
    /// 4-component floating point vector.
    /// </summary>
    Vector4 = 4,

    /// <summary>
    /// 4x4 single-precision matrix.
    /// </summary>
    Matrix4x4 = 5
}

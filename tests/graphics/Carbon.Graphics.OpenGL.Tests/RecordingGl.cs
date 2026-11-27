// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Tests;

/// <summary>
/// A Silk <see cref="GL"/> with no driver behind it: buffer entry points record what they are asked to do, every
/// other entry point is a no-op. Lets buffer upload rules run in xUnit (audit round 2, PERF-11). Not thread-safe:
/// one instance at a time per test.
/// </summary>
internal static unsafe class RecordingGl
{
    /// <summary>One recorded <c>glBufferData</c> or <c>glBufferSubData</c> call.</summary>
    /// <param name="Name">Entry point name.</param>
    /// <param name="Target">Buffer target.</param>
    /// <param name="Offset">Byte offset (sub-data only).</param>
    /// <param name="Size">Byte size.</param>
    /// <param name="HasData">Whether a data pointer was passed (orphaning passes none).</param>
    internal readonly record struct BufferCall(string Name, uint Target, long Offset, long Size, bool HasData);

    private static readonly StubContext s_context = new();

    [ThreadStatic]
    private static List<BufferCall>? s_calls;

    /// <summary>Gets the calls recorded on this thread since <see cref="Create"/>.</summary>
    public static List<BufferCall> Calls => s_calls ??= [];

    /// <summary>Creates a recording GL and clears the recorded calls.</summary>
    /// <returns>The GL.</returns>
    public static GL Create()
    {
        Calls.Clear();
        return new GL(s_context);
    }

    [UnmanagedCallersOnly]
    private static void BufferData(uint target, nint size, void* data, uint usage)
    {
        _ = usage;
        Calls.Add(new BufferCall("glBufferData", target, 0, size, data is not null));
    }

    [UnmanagedCallersOnly]
    private static void BufferSubData(uint target, nint offset, nint size, void* data) =>
        Calls.Add(new BufferCall("glBufferSubData", target, offset, size, data is not null));

    [UnmanagedCallersOnly]
    private static void GenBuffers(int count, uint* buffers)
    {
        for (int i = 0; i < count; i++)
        {
            buffers[i] = (uint)(i + 1);
        }
    }

    [UnmanagedCallersOnly]
    private static nint Noop() => 0;

    private sealed class StubContext : INativeContext
    {
        public nint GetProcAddress(string proc, int? slot = null) => proc switch
        {
            "glBufferData" => (nint)(delegate* unmanaged<uint, nint, void*, uint, void>)&BufferData,
            "glBufferSubData" => (nint)(delegate* unmanaged<uint, nint, nint, void*, void>)&BufferSubData,
            "glGenBuffers" => (nint)(delegate* unmanaged<int, uint*, void>)&GenBuffers,
            _ => (nint)(delegate* unmanaged<nint>)&Noop,
        };

        public bool TryGetProcAddress(string proc, out nint addr, int? slot = null)
        {
            addr = this.GetProcAddress(proc, slot);
            return true;
        }

        public void Dispose()
        {
        }
    }
}

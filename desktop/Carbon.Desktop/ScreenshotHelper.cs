// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Silk.NET.OpenGL;

namespace Carbon.Desktop;

/// <summary>
/// Utility for capturing OpenGL framebuffer pixels and exporting uncompressed standard BMP image files.
/// </summary>
public static class ScreenshotHelper
{
    /// <summary>
    /// Reads pixels directly from the active OpenGL framebuffer and saves to a 24-bit uncompressed BMP file.
    /// </summary>
    /// <param name="gl">The active OpenGL API instance.</param>
    /// <param name="width">Framebuffer width in pixels.</param>
    /// <param name="height">Framebuffer height in pixels.</param>
    /// <param name="filePath">Target destination file path.</param>
    public static void Capture(GL gl, int width, int height, string filePath)
    {
        ArgumentNullException.ThrowIfNull(gl, nameof(gl));

        if (width <= 0 || height <= 0 || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

        int rowStride = ((width * 3) + 3) & ~3;
        int imageSize = rowStride * height;
        int fileSize = 54 + imageSize;

        byte[] bmpData = new byte[fileSize];

        // 1. BMP File Header (14 bytes)
        bmpData[0] = (byte)'B';
        bmpData[1] = (byte)'M';
        BitConverter.GetBytes(fileSize).CopyTo(bmpData, 2);
        BitConverter.GetBytes(54).CopyTo(bmpData, 10);

        // 2. DIB Header / BITMAPINFOHEADER (40 bytes)
        BitConverter.GetBytes(40).CopyTo(bmpData, 14);
        BitConverter.GetBytes(width).CopyTo(bmpData, 18);
        BitConverter.GetBytes(height).CopyTo(bmpData, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmpData, 26);
        BitConverter.GetBytes((short)24).CopyTo(bmpData, 28);
        BitConverter.GetBytes(0).CopyTo(bmpData, 30);
        BitConverter.GetBytes(imageSize).CopyTo(bmpData, 34);
        BitConverter.GetBytes(2835).CopyTo(bmpData, 38);
        BitConverter.GetBytes(2835).CopyTo(bmpData, 42);
        BitConverter.GetBytes(0).CopyTo(bmpData, 46);
        BitConverter.GetBytes(0).CopyTo(bmpData, 50);

        // 3. Read pixels directly from OpenGL (BGR format matches BMP specification natively)
        gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
        gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Bgr, PixelType.UnsignedByte, bmpData.AsSpan(54));

        File.WriteAllBytes(filePath, bmpData);
    }
}

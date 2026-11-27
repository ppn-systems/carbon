// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Primitives;

namespace Carbon.Infrastructure.Persistence;

/// <summary>
/// Converts the accounts table's <c>password_hash</c> blob to and from <see cref="Bytes32"/>, for both account stores.
/// The column stays a blob.
/// </summary>
internal static class PasswordHashColumn
{
    /// <summary>
    /// Reads a stored hash. A blob that is not exactly 32 bytes (damaged or foreign) becomes <see cref="Bytes32.Zero"/>,
    /// which never verifies, so the sign-in fails instead of throwing.
    /// </summary>
    public static Bytes32 Read(byte[] blob) => blob is { Length: Bytes32.Size } ? new Bytes32(blob) : Bytes32.Zero;

    /// <summary>Writes a hash as its 32-byte blob.</summary>
    public static byte[] Write(in Bytes32 hash) => hash.ToByteArray();
}

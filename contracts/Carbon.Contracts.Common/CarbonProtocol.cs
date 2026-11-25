// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Common;

/// <summary>
/// Global Carbon wire protocol metadata shared across all modules.
/// </summary>
public static class CarbonProtocol
{
    /// <summary>Current wire protocol version negotiated during authentication.</summary>
    public const ushort Version = 1;
}

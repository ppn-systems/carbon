// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Configuration.Sync;

/// <summary>State of a <see cref="ConfigSyncClient"/>.</summary>
public enum ConfigSyncState : byte
{
    /// <summary>No manifest received yet.</summary>
    AwaitingManifest = 0,

    /// <summary>Documents were requested and are being received.</summary>
    Downloading = 1,

    /// <summary>Every manifest document is verified; <see cref="ConfigSyncClient.Catalog"/> is current.</summary>
    Ready = 2,

    /// <summary>The server sent an invalid manifest or a document kept failing verification.</summary>
    Faulted = 3,
}

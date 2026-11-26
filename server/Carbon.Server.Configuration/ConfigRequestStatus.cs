// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Server.Configuration;

/// <summary>Outcome of <see cref="ConfigRepository.HandleRequestAsync"/>.</summary>
public enum ConfigRequestStatus : byte
{
    /// <summary>The request was valid; the document stream was produced.</summary>
    Ok = 0,

    /// <summary>The request lists more names than <see cref="Contracts.Config.ConfigProtocolLimits.MaxDocuments"/>.</summary>
    TooManyNames = 1,

    /// <summary>A name breaks the <see cref="Contracts.Config.ConfigDocumentName"/> rule.</summary>
    InvalidName = 2,

    /// <summary>A name is listed twice.</summary>
    DuplicateName = 3,

    /// <summary>A well-formed name is not in the current manifest.</summary>
    UnknownName = 4,

    /// <summary>The client synced against an older manifest; send it the current manifest again.</summary>
    StaleManifest = 5,
}

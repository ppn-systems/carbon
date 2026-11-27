// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Simulation.ECS;

/// <summary>
/// An attachment that <see cref="EntityAttachmentStore{T}"/> reuses for the next entity instead of letting it be collected
/// (ADR-044). When its entity is despawned, or the attachment is removed or replaced, the store calls
/// <see cref="ResetForReuse"/> and keeps the instance; the next <see cref="EntityAttachmentStore{T}.GetOrAdd"/> hands it
/// out before calling the factory.
/// </summary>
/// <remarks>
/// Hold the attachment only for the call that fetched it from the store: a reference kept past despawn may see the
/// next entity's state. Never keep it in a field or a closure (guarded by <c>AttachmentReuseGuardTests</c>).
/// </remarks>
public interface IReusableAttachment
{
    /// <summary>
    /// Brings the instance to the state of a freshly constructed one. Collections keep their capacity but are emptied;
    /// arrays are zeroed.
    /// </summary>
    void ResetForReuse();
}

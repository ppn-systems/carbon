// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Maps.Building;

/// <summary>
/// Server-side policy deciding who may modify (deliver to, build, cancel, deconstruct) an owned structure or site.
/// </summary>
public interface IBuildPermissions
{
    /// <summary>Evaluates whether <paramref name="actorId"/> may modify something owned by <paramref name="ownerId"/>.</summary>
    /// <param name="actorId">Acting player id.</param>
    /// <param name="ownerId">Owner player id (0 = unowned).</param>
    /// <returns><c>true</c> when allowed.</returns>
    bool CanModify(ulong actorId, ulong ownerId);
}

/// <summary>
/// Default permission policy: unowned structures are public, otherwise only the owner and players the owner granted.
/// </summary>
public sealed class BuildPermissionTable : IBuildPermissions
{
    private readonly Dictionary<ulong, HashSet<ulong>> _grants = new();

    /// <summary>Allows <paramref name="guestId"/> to modify everything owned by <paramref name="ownerId"/>.</summary>
    /// <param name="ownerId">Owner player id.</param>
    /// <param name="guestId">Guest player id.</param>
    public void Grant(ulong ownerId, ulong guestId)
    {
        if (!_grants.TryGetValue(ownerId, out HashSet<ulong>? guests))
        {
            guests = [];
            _grants[ownerId] = guests;
        }

        _ = guests.Add(guestId);
    }

    /// <summary>Revokes a previous <see cref="Grant"/>.</summary>
    /// <param name="ownerId">Owner player id.</param>
    /// <param name="guestId">Guest player id.</param>
    /// <returns><c>true</c> when a grant was removed.</returns>
    public bool Revoke(ulong ownerId, ulong guestId) =>
        _grants.TryGetValue(ownerId, out HashSet<ulong>? guests) && guests.Remove(guestId);

    /// <inheritdoc/>
    public bool CanModify(ulong actorId, ulong ownerId) =>
        ownerId == 0 || actorId == ownerId ||
        (_grants.TryGetValue(ownerId, out HashSet<ulong>? guests) && guests.Contains(actorId));
}

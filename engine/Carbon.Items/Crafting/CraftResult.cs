// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Items.Crafting;

/// <summary>
/// Server-authoritative outcome of a crafting operation: the wire-level <see cref="OpStatus"/> plus a detailed reason.
/// </summary>
/// <param name="Status">Canonical status sent to clients in <see cref="MutationResult"/>.</param>
/// <param name="Failure">Detailed reason code (<see cref="CraftFailure.None"/> on success).</param>
public readonly record struct CraftResult(OpStatus Status, CraftFailure Failure)
{
    /// <summary>Gets the successful result.</summary>
    public static CraftResult Ok => new(OpStatus.Ok, CraftFailure.None);

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsOk => this.Status == OpStatus.Ok;

    /// <summary>
    /// Creates a failed result with the canonical status for <paramref name="failure"/>.
    /// </summary>
    /// <param name="failure">Failure reason.</param>
    /// <returns>The failed result.</returns>
    public static CraftResult Fail(CraftFailure failure) => new(ToStatus(failure), failure);

    private static OpStatus ToStatus(CraftFailure failure) => failure switch
    {
        CraftFailure.None => OpStatus.Ok,
        CraftFailure.InventoryFull => OpStatus.InventoryFull,
        CraftFailure.WrongStation => OpStatus.OutOfRange,
        CraftFailure.LevelTooLow or CraftFailure.SkillTooLow => OpStatus.Forbidden,
        CraftFailure.MissingIngredients or CraftFailure.InsufficientFunds => OpStatus.Conflict,
        CraftFailure.InvalidRecipe => OpStatus.Invalid,
        _ => OpStatus.Invalid
    };
}

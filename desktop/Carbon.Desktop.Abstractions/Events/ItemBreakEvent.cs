// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Desktop.Abstractions.Events;

/// <summary>
/// The local player's item broke at 0 durability and was destroyed (ADR-035); the renderer throws shatter shards here.
/// </summary>
/// <param name="WorldPosition">The player's world position when the break arrived.</param>
/// <param name="ItemId">Item archetype id of the destroyed item.</param>
/// <param name="UpgradeLevel">Its enhancement level (an enhanced item throws more shards).</param>
public readonly record struct ItemBreakEvent(Vector2 WorldPosition, uint ItemId, byte UpgradeLevel);

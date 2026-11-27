// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Contracts.Entities;

namespace Carbon.Application.World.Actors;

/// <summary>What the server knows about a replicated NPC or monster: identity, home, stats, drops and reward.</summary>
internal readonly record struct NpcActorMeta(
    EntityKind Kind,
    ushort ArchetypeId,
    string Name,
    ushort Level,
    Vector2 HomePos,
    float MaxHealth,
    float MoveSpeed,
    float Radius,
    float AttackDamage,
    uint WindupTicks,
    long ChunkKey,
    uint DropItemId,
    int DropQuantity,
    uint ExperienceReward,
    float AttackRange = 44f,
    float ProjectileSpeed = 0f,
    byte ProjectileArchetype = 0,
    string? LootTableId = null,
    float PatrolRadius = 64f);

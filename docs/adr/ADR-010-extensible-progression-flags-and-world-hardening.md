# ADR-010: Extensible Progression Flags, Binary Codec & Server Hardening

- **Date**: 2026-11-26
- **Status**: Accepted
- **Scope**: `contracts/`, `engine/`, `server/`, `desktop/`, `tests/`

## Context

Following ADR-009, full-stack DDD/SOLID auditing identified several structural bottlenecks and correctness risks:
1. `CharacterRecord`, SQLite/PostgreSQL schemas, `PlayerStatsSync`, and `AuthoritativeWorld` hardcoded 4 attribute columns (`stat_vit..stat_agi`) and 3 skill rank columns (`skill1_rank..skill3_rank`), plus omitted `HeroArchetypeClass` and `[Flags]` character progression/skill unlock bitmasks.
2. `CharacterPointTarget` and `AbilityInputEncoding` coupled open skill/attribute IDs to closed enums and magic byte offsets (`210+`, `220+`) across server and client.
3. Several hot-path and concurrency bugs existed: shared scratch lists read outside `lock (_gate)`, unbuffered `_pending*` queues overwritten/cleared between ticks, non-finite `NaN`/`Infinity` input lack of validation, predictable `% 5UL` crits, `AuthService` skipping `GrantStarterLoadout` for `Vanguard (0)`, and Level 16+ SP overflow blocking HUD telemetry.

## Decision

1. **Flags & Open-ID Protocol (`contracts/`)**:
   - Introduce `[Flags] CharacterTraits : ulong` (`Carbon.Contracts.Common`) and `[Flags] CharacterSkillMask : ulong` (`Carbon.Contracts.Entities`).
   - Replace closed `CharacterPointTarget` on `EntityAllocatePoint` with `PointAllocationKind` + `ushort TargetId`.
   - Extend `CharacterSummary`, `CharacterEnterWorld`, and `PlayerStatsSync` with `ClassId`, `Traits`, `UnlockedSkillMask`, and versioned `byte[] ProgressionBlob` + `ushort[] CooldownSkillIds` / `float[] CooldownRemainingSeconds`.
   - Centralize projectile cast encoding in `AbilityInputEncoding`.
2. **Data-Driven Domain Catalogs & Codec (`engine/`)**:
   - Add `SkillCatalog` (`DefinitionRegistry<ushort, SkillDefinition>`), `CharacterProgressionState`, and `CharacterProgressionBinaryCodec` (AOT-safe little-endian framing modeled after `InventoryBinaryCodec`).
   - Add `[Flags] ToolCapabilities` on `ItemArchetype` and `[Flags] NpcCapabilities` on `NpcArchetypeSpec`.
   - Fix `StatSheet._dirtyMask` (`byte` -> `ulong`), cap `GetTotalSkillPointsForLevel` at `SkillCatalog.TotalLearnableRanks`, equip all 3 starter classes uniformly in `GrantStarterLoadout`, and guard `TryCraftInBag` to bag/hotbar indices `0..35` with `UpgradeLevel == 0`.
3. **Persistence Migration `0004`, Concurrency & UI/UX (`server/`, `desktop/`)**:
   - Migrate SQLite & PostgreSQL characters to `class_id`, `character_flags`, `skill_flags`, and `progression_blob` (losslessly packing any legacy `0003` rows inside migration `0004`), and replace `IWorldOutboundSink.SendPlayerStatsSync`'s 24 positional parameters with `in PlayerStatsSnapshot`.
   - Double-buffer `_pending*` outbound queues in `AuthoritativeWorld`, eliminate all unsynchronized scratch reads outside `lock (_gate)`, pool `EntityDeltaBatch` arrays, guard `float.IsFinite`, and fix client UI clipping, door sync, scrollbars, and generic consumable checks.

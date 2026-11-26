# ADR-007: DDD, SOLID Hardening, Thread Safety, and MMORPG System Completeness

- **Status**: Accepted
- **Date**: 2026-11-26
- **Branch**: `feature/aot-backend-and-desktop-arch`

## Context

Following the implementation of the 40-slot inventory, visible modular armor, ground loot drops, forge enhancement, active skills, and progression vitals (see [ADR-006](ADR-006-rpg-progression-equipment-ground-loot-and-skills.md)), a cross-layer architectural and gameplay audit identified key structural and gameplay gaps:

1. **Domain & SOLID Debt**:
   - Item combat/harvest/armor/consumable attributes were scattered across parallel `switch` tables instead of living inside `ItemArchetype` (OCP).
   - Enhancement level (`0..10`) was bit-packed into `ItemSlot.InstanceId & 0xFF`, breaking `ItemSlot.IsStackable` invariants and erasing unique identity (LSP).
   - `IAuthoritativeWorld` acted as a 22-method monolith and `Carbon.Handlers` referenced `Carbon.Application` directly (ISP / DIP).
   - `AuthoritativeWorld` exceeded 3,200 lines in a single file with unlocked shared scratch collections accessed outside `lock (_gate)`, un-cleared `EntityDespawnedEvent` buffers, and per-tick persistence byte-array allocations.
   - Client `NalixMultiplayerSession` mutated `InventoryContainer`, vitals, and interpolation buffers across background socket callbacks and the main render loop without lock synchronization or immutable per-frame snapshots.
2. **Rendering & HUD Debt**:
   - `ModularCharacterOverlays` and `VectorRigRenderer` hardcoded `StarterGameCatalog` IDs and visual colors instead of reading declarative `.cvg.json` metadata.
   - `HudOverlayRenderer` batched geometry and text globally across overlapping layers (causing background HUD text to bleed over the Inventory Modal), leaked a per-frame `CombatFeedbackTracker` fallback allocation, and had coordinate collisions at `1280x720`.
3. **MMORPG Completeness Gaps**:
   - Ground loot spawned on `PlayerLayer` (causing monsters to aggro/attack loot and block movement) and lacked stack merging, partial-pickup client count sync, and ownership grace.
   - Player death delayed teleport by one tick without resetting monster aggro/threat or clearing combat momentum.
   - Initial `CharacterEnterWorld` omitted player stats and hotbar selection; remote `SkillCastBroadcast` lost `AbilitySlot` in client rendering; `[Tab]` opened the bag instead of target lock-on; crafting could overflow into armor slots `36..39`; structure placement lacked kit checks; and regrown props did not notify clients.

## Decision

1. **Rich Data-Driven Engine Domain Models (`engine/Carbon.Items`)**:
   - Store combat/harvest/armor/consumable attributes and hints directly on `ItemArchetype`, plus monster/NPC scaling specs in `WorldActorCatalog` and typed `SkillSpec` definitions in `CharacterProgressionPolicy`.
   - Move enhancement level to explicit `ItemSlot.UpgradeLevel` with `InventoryBinaryCodec` Format v2 (keeping Format v1 deserialization backward-compatible) and enforce `MaxStackSize == 1` non-stacking + `PlayerInventoryLayout.TryCraftInBag`.
2. **Protocol & Port Segregation (`contracts/`, `server/Carbon.Abstractions`, `server/Carbon.Handlers`)**:
   - Add append-only vitals/hotbar fields to `CharacterEnterWorld` (`[SerializeOrder(11..21)]`), `MaxHealth` to `EntityEquipmentChanged` (`[SerializeOrder(8)]`), `OpStatus.EnhancementFailed` / `InventoryFull`, and shared `AbilityInputEncoding`.
   - Segregate `IAuthoritativeWorld` into `IWorldSessionGateway`, `IWorldEntityCommandSink`, `IWorldInventoryService`, `IWorldMapService`, and `IWorldSimulationLoop`, removing `Carbon.Handlers -> Carbon.Application`.
   - Split `AuthoritativeWorld` into cohesive partial files, eliminate off-lock shared list races (`[ThreadStatic]` scratch lists + in-lock snapshots), drain `EntityDespawnedEvent`, and defer DB byte serialization to logout/flush ticks.
3. **Full MMORPG Gameplay Loop**:
   - Place `LootDrop` on non-solid `LootLayer` trigger colliders; ignore `IsTrigger` / healthless entities in AI target validation; support merge-nearby drops, partial-pickup broadcasts, and owner grace.
   - Resolve immediate player respawn at loaded sanctuary chunks with spawn protection and monster aggro clear; enforce LoS raycasts on attacks/skills; keep `ReturnHome` monsters evade-immune; add separate potion cooldown; enforce building kit consumption & collision checks; broadcast prop respawns; and render remote skill VFX, ground target reticle, XP popups, `[Tab]` target lock, drag-and-drop inventory with stat comparison & forge deltas, and 3-pass Z-ordered collision-free HUD.

## Consequences

- Backward wire compatibility is preserved via append-only `[SerializeOrder]` and codec Format v1->v2 migration.
- Hot paths remain strictly allocation-free and thread-safe across both server tick/handler threads and client socket/OpenGL threads.

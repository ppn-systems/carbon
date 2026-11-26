# ADR-006: RPG Progression, 40-Slot Minecraft-Style Equipment, Ground Loot & Active Skills

- **Status**: Accepted
- **Date**: 2026-11-26
- **Scope**: `contracts/`, `engine/Carbon.Items/`, `server/`, `desktop/`, `resources/art/`

## Context

To transform Carbon from a prototype fixed-class sandbox into a complete MMORPG loop:
1. Characters should share one modular base adventurer rig customized by hair style, hair color, face style, and skin tone (`CharacterAppearance` bit-packed into `byte AppearancePreset`), with equipped Head/Chest/Legs/Feet armor visibly layered onto the rig bones.
2. Players need a 40-slot Minecraft-style inventory (`0..8` hotbar, `9..35` 3x9 backpack, `36..39` armor slots), item enhancement (`+0..+10`), ground loot drops on mob/prop defeat (`EntityKind.LootDrop`), Level/XP progression, Stamina & Mana pools with 3 active skills, and chunk-scoped prop durability bars.

## Decision

1. **40-Slot Unified Inventory**: `PlayerInventoryLayout` partitions `InventoryContainer(40)` into `0..8` Hotbar, `9..35` Backpack, and `36..39` Armor (`Head`, `Chest`, `Legs`, `Feet`), keeping `InventoryBinaryCodec` backward-compatible with legacy 27-slot blobs.
2. **Item Enhancement Encoding**: Upgrade levels `+0..+10` are stored in `ItemSlot.InstanceId & 0xFFUL` (`ItemEnhancementPolicy`) and replicated over the wire via append-only `[SerializeOrder]` fields without breaking older codecs.
3. **Ground Loot Entities**: `AuthoritativeWorld` spawns authoritative `EntityKind.LootDrop` entities with `LootDropComponent` and `LootExpirationSystem` instead of auto-depositing drops directly into inventories; players pick up loot automatically at close range (`<= 30px`) or on `[E]` press (`EntityPickupLoot`).
4. **Customization & Armor Layering**: `CharacterAppearance` packs 4 two-bit traits (`HairStyle`, `HairColor`, `FaceStyle`, `SkinTone`) into the existing `byte AppearancePreset`. `VectorRigRenderer` draws `base_hero.cvg.json` plus procedural hair/face and armor overlays directly onto skeleton bone pivots.
5. **Progression & Persistence**: SQLite and PostgreSQL schemas receive idempotent migration `0002_rpg_progression` adding `experience`, `current_mana`, `max_mana`, `current_stamina`, and `max_stamina`.

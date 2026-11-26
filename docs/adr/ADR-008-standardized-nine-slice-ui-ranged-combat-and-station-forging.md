# ADR-008: Standardized 9-Slice UI Skin, Ranged/Mage Combat & Station-Bound Forging

- **Status**: Accepted
- **Date**: 2026-11-26
- **Deciders**: Carbon Engineering Team

## Context

Three architectural and gameplay gaps remained in the desktop client and combat loop:

1. **UI Consistency & Login/Roster/Workshop Ergonomics**: HUD panels, modal windows, login/registration screens, and character selection relied on flat unstyled rectangles while `C:\Users\nguye\Downloads\X\panels\` provided uniform `96x96` two-color pixel-art panel templates ready for nine-slice scaling. Meanwhile, the login/register form lacked an explicit Sign-In vs. Create Account workflow with password confirmation, and the Crafting panel was a cramped left-docked text list.
2. **Hero Archetype Variety & Ranged/Magic Combat**: Players spawned only as melee swordsmen, and magic/ranged combat (`ArcaneNova` cone only) lacked physical ballistic projectiles (`EntityKind.Projectile`) for hunter bows, mage staffs, and ranged monsters.
3. **Misplaced Forging Coupling**: Gear enhancement (`+1..+10`) lived directly inside the personal Inventory modal (`[I]`) instead of requiring proximity to an authoritative blacksmith NPC or crafted workstation anvil.

## Decisions

### 1. Dual-Layer 9-Slice `CarbonUiSkin` under `resources/ui/panels/`
- Curated `96x96` pixel panel templates (`0.png`, `1.png`, `4.png`, `9.png`, `12.png`, `14.png`, `15.png`, `20.png`) are stored under `resources/ui/panels/`, keeping `resources/art/` 100% pure `.cvg.json` vector art (`0` PNG/JPG images).
- `CarbonUiSkin` splits each loaded template via flood-fill from its center `(48, 48)` into `BodyTexture` and `FrameTexture` GPU textures so `NineSliceRenderer.DrawNineSlice` renders dark obsidian translucent interiors underneath crisp independently tinted Gold, Emerald, Cyan, or Crimson pixel borders with zero per-frame allocations.

### 2. Append-Only `CharacterCreate.StartingClass` & Authoritative Projectiles
- Append `[SerializeOrder(2)] public byte StartingClass { get; set; }` onto `CharacterCreate` backed by `HeroArchetypeClass` (`Vanguard = 0`, `Ranger = 1`, `Arcanist = 2`).
- Server combat in `AuthoritativeWorld` spawns authoritative `EntityKind.Projectile` actors when firing `HuntingBow`, `CrystalBow`, `ArcaneStaff`, `CrystalStaff`, `SkeletonArcher`, or `AshWraith` attacks, sweeping line-of-sight obstacle raycasts and target hit tests each fixed tick before broadcasting hits and stats.

### 3. Workshop Crafting Modal (`[C]`) & Station-Bound Blacksmith/Anvil Forging (`[U]`)
- Personal bag (`InventoryModalWidget`, `[I]`) is decoupled from enhancement and displays 40 slots, armor paper-doll, live character stats, and item actions (`[EQUIP / USE]`, `[DROP 1]`, `[DROP ALL]`).
- Crafting opens a centered two-pane `CraftingModalWidget` (`[C]`) with category filters, craftable toggle, scrollable blueprints, stat preview, and `[CRAFT x1]` / `[CRAFT x5]`.
- Item enhancement moves to `ForgeAnvilModalWidget` (`[U]`) and requires server-side proximity validation in `AuthoritativeWorld.TryUpgradeInventoryItem` against either a friendly settlement NPC (`MasterBlacksmith` / town NPC) or a player-placed `StructureType.ForgeAnvil` (`StructureCategory.Station`), returning `OpStatus.OutOfRange` otherwise.

## Consequences

- Client and server retain full wire backward compatibility (`StartingClass = 0` default matches Vanguard starter loadouts and existing tests).
- `Carbon.Desktop.Rendering` consumes `Carbon.Graphics.Assets` for `ImageDecoder` while preserving strict engine/server/contracts layer boundaries.

# ADR-009: Attribute & Skill Mastery Points, Clean 9-Slice UI Skin, Settings Modal, and Strict Combat/Harvest Reach

- **Status**: Accepted
- **Date**: 2026-11-26
- **Deciders**: Carbon Architecture Team

## Context

Gameplay testing and screenshot audits revealed five structural and UX gaps:
1. **Panel Nine-Slice Proportions**: `0.png` (`96x96`, `4x` upscaled `24x24` pixel art) contained transparent outer/inner moats around corners, while `9.png` (`Slot`) and `1.png` (`Button`) placed heavy ornamental bars across small (`44x44` / `30px`) surfaces, obscuring item icons and button text. Additionally, HUD widgets drew duplicate opaque primitives over `CarbonUiSkin`.
2. **Oversized Harvest & Melee Reach**: `DefaultInteractRangeTiles = 5.5f` (`176px`) allowed axes and pickaxes to chop trees or mine boulders up to 6 tiles away.
3. **Progression Agency & Non-Free Skills**: Leveling up passively scaled vitals without player choice, while all three combat skills (`Whirlwind`, `Arcane Nova`, `Vital Surge`) unlocked automatically at Level 1.
4. **HUD Clutter & Missing Settings/Guide**: Bottom skill bar, static yellow action-hint strip, and permanent keybind banner stacked vertically over the screen, with no `[ESC]` Settings & Controls Guide modal to toggle Minimap (`[M]`), Floating Combat Numbers, Telemetry, or Camera Zoom.
5. **Visual & Animation Fidelity**: Hero overlays (`ModularCharacterOverlays.cs`), early tools, flat bushes (`PropArt.cs`), and linear swing curves needed richer silhouettes, anticipation/follow-through easing, dynamic bowstring draw, and additional RPG monsters/items inspired by the reference packs.

## Decision

1. **Solid-Interior 2x Downsampled UI Skin (`CarbonUiSkin`)**:
   - Use `10.png` (Ornate Window), `5.png` (Ornate Highlight), and `15.png` (Card, Button, Slot, Inset) downsampled `2x` to `48x48` with `SplitBodyAndFrame` filling the entire interior bounding region solidly so zero terrain bleed-through or edge-bar overlap occurs.
2. **Authoritative Attribute Points (AP) & Skill Mastery Points (SP)**:
   - Earn `4 AP` per level above Lv.1 (`Vitality`, `Wisdom`, `Power`, `Agility`) and `1 SP` at Lv.1 plus `+1 SP` per level (`Skill1..3`, Ranks `0..5`). Rank `0` skills remain locked and cannot be cast.
   - Add wire packet `EntityAllocatePoint` (`EntityOpCode.AllocateCharacterPoint = 0x1204`) with `CharacterPointTarget`, extend `PlayerStatsSync` (`0x1235`, fields `14..22`), and persist via idempotent migration `0003_stat_and_skill_points` on both SQLite and PostgreSQL.
3. **Strict Domain-Separated Reach**:
   - Separate `BuildInteractRangeTiles = 3.5f` (`112px`), `ToolHarvestRangePx = 52f`, and `MeleeStrikeRangePx = 56f` in `AuthoritativeWorld`, eliminating wide `[-1..1]` neighbor searches around distant clicks.
4. **Unified HUD, Skill Tome (`[K]`), Settings/Controls Modal (`[ESC]`), & Expanded World Content**:
   - Unify Skill Bar `[Q/R/V]`, 9-Slot Hotbar `[1..9]`, and `BAG [I] / SKILL [K] / CRAFT [C] / FORGE [U] / CFG [ESC]` onto one clean bottom bar; default bottom keybind strip to `false`.
   - Persist client preferences AOT-safely via `ClientUiSettings` (`ui_settings.cfg`).
   - Add `RoastMeat`, `ArcanePie`, `DemonCleaver`, `ShamanStaff`, `OrcShaman`, and `CrimsonDemon`, refine `.cvg.json` shapes and non-linear swing/bowstring/staff animations, and outline/shade bushes and tree canopies.

## Consequences

- Zero warnings/errors under `TreatWarningsAsErrors` and full Native AOT compatibility (zero reflection settings serialization).
- Existing Level > 1 saved characters automatically receive retroactive unspent AP & SP computed purely from `Level - sum(allocated)`.

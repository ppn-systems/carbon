# ADR-021: Data-Driven Character Progression, Combat Stats and Attribute Reset

- **Date**: 2026-11-27
- **Status**: Accepted
- **Scope**: `resources/config/progression.json`, `engine/Carbon.Items/Progression`, `contracts/Carbon.Contracts.Entities`,
  `server/Carbon.Application/World`, `desktop/` character panel, `tests/`
- **Design**: [`docs/progression.md`](../progression.md) (evaluation, formulas, targets, simulation results)

## Context

The owner asked for many more character stats (defense, crit chance, …) that players allocate, with a reasonable
economy for points and experience per level. The evaluation in `docs/progression.md` §1 found:

- the XP curve (`100 + 75 × (level − 1)`, `engine/Carbon.Items/Catalog/CharacterProgressionPolicy.cs`) reaches level 99
  in about 2.6 hours of play;
- four linear, uncapped attributes (`PrimaryAttributeId`), a hard-coded ×1.5 crit, level-independent uncapped defense,
  no magic resist, evasion or player attack speed;
- every table compiled into code, which ADR-014 lists as debt, and the desktop recomputing point budgets with its own
  copy.

This changes a wire enum, adds a synced config document and changes how saved allocations are interpreted, so it
needs an ADR (`AGENTS.md` rule 5).

## Decision

1. **D1 — One config document.** `resources/config/progression.json` holds the XP curve (anchors, geometric
   interpolation), the kill XP formula with a level-difference multiplier, the attribute point brackets and
   milestones, base / per-level / per-attribute stat blocks, caps, mitigation and evasion curves, and the respec price.
   `ProgressionConfig.LoadFromJson` (`engine/Carbon.Items/Progression`) rejects unknown fields, negative growth,
   gaps in brackets, non-increasing XP anchors and out-of-range caps. The server fails fast at boot and rejects an
   invalid hot reload (ADR-014 D7/D12). It is **not** server-only: clients use it to preview stats, exactly like
   `needs.json`. Gameplay code reads only this instance; no balance table stays in `CharacterProgressionPolicy`.
2. **D2 — Five attributes.** `PrimaryAttributeId` keeps `Vitality = 0`, `Wisdom = 1`, `Power = 2`, `Agility = 3` and
   adds `Dexterity = 4`. Ids are persisted in the progression blob (`CharacterProgressionBinaryCodec`), so they are
   append-only and never renumbered. Old clients that do not know id 4 simply never allocate it.
3. **D3 — Derived stats.** `DerivedCharacterStats` gains magic resist, crit chance, crit damage, attack speed, evasion
   rating and HP/MP regen (appended optional members; existing positional members unchanged). Crit chance, crit damage,
   attack speed and move speed are capped; defense, magic resist and evasion use
   `min(cap, rating / (rating + K + perAttackerLevel × attackerLevel))`.
4. **D4 — Damage kinds.** A monster with any `Spell` attack is a caster; its `Spell` and `Projectile` attacks are
   magical (mitigated by magic resist), all other monster damage is physical. Staff (`MagicFocus`) shots scale with
   spell power. No new data is added to `monsters.json`.
5. **D5 — Deterministic rolls.** Crits and dodges use `CombatRoll.Roll(chance, nonce, salt)`, a hash of the existing
   per-character `CombatRollNonce`; crit and evasion use different salts.
6. **D6 — Attribute reset on the existing packet.** `PointAllocationKind` gains `ResetAttributes = 2`, sent in the
   existing `EntityAllocatePoint` (`0x1204`); `TargetId` and `Amount` are ignored. No new opcode and no packet layout
   change. The server tick-queues it (ADR-017), charges `respec` gold through the wallet ledger with a new
   `CurrencyReason`, and rejects it without change when the player cannot pay. Free through `respec.freeThroughLevel`.
   *Amended by [ADR-034](ADR-034-retire-gold-attribute-reset.md): the reset is retired from the UI and the wire until a
   respec-scroll item replaces it.*
7. **D7 — Budget safety.** Every level grants at least the old 4 points, so saved characters keep valid allocations.
   If a future config leaves a character over budget, the server refunds the allocation on load (free). Allocation
   requests are rejected when no point is unspent (`Conflict`) or the attribute is unknown (`Invalid`), as today.
8. **D8 — No persistence migration.** Allocations already persist per attribute id in the progression blob; level
   and experience columns are unchanged. A character keeps its level and its experience into the level; the new curve
   applies from now on.

## Alternatives Considered

- **A closed-form XP formula** (`a × level^p + b`). Rejected: kill XP is non-linear (monster pool changes by band,
  stat curves, the XP clamp), so one power law cannot hit per-bracket targets. Anchors are readable and tunable per
  bracket, and interpolation keeps the curve smooth and monotonic.
- **Keep the old 650 kill XP cap.** Rejected: kill XP stopped growing at level ~65 while monster HP kept growing,
  forcing a flat curve where late levels give no more reward per effort.
- **A new respec packet / opcode.** Rejected: one more enum value on the allocation packet is enough and keeps the
  opcode table unchanged.
- **Damage type per attack in `monsters.json`.** Rejected for now: it overlaps the monster AI work running in
  parallel; the caster rule needs no new data and can be replaced by an explicit field later.
- **Random (`System.Random`) crits.** Rejected: non-deterministic; replays and tests could not reproduce combat.
- **A forced free respec for everyone.** Rejected: unnecessary, since no saved allocation becomes over budget.

## Consequences

- Positive: balance lives in one validated, hot-reloadable document shared by server and client; the time-to-level
  simulation (`TimeToLevelSimulationTests`) fails CI when a config edit breaks the pacing targets.
- Positive: crit, tank, caster and tempo builds exist, with caps and diminishing returns that keep high-level content
  meaningful.
- Trade-off: leveling becomes ~35× slower than before (by design: ~100 hours to the cap instead of ~2.6).
- Trade-off: existing characters keep their allocations but their derived stats change with the new per-point values.
- Debt: the damage-kind rule is implicit (D4); the simulation models melee builds only.

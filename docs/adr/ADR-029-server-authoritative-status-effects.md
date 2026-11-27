# ADR-029: Server-Authoritative Status Effects

- **Date**: 2026-11-27
- **Status**: Accepted
- **Scope**: `engine/Carbon.Combat/Statuses`, `engine/Carbon.Primitives/Random`, `resources/config/status_effects.json`,
  `contracts/Carbon.Contracts.Items`, `server/Carbon.Abstractions`, `server/Carbon.Application/World`,
  `server/Carbon.Infrastructure/Persistence`, `server/Carbon.Host`, `desktop/`, `resources/localization`, `tests/`
- **Builds on**: [ADR-014](ADR-014-server-authoritative-config.md) (config documents),
  [ADR-015](ADR-015-survival-needs-persistence.md) (needs poison), [ADR-017](ADR-017-tick-queued-world-commands.md)
  (tick order), [ADR-018](ADR-018-rest-need-sleep-and-sit.md) (rest), [ADR-026](ADR-026-server-secret-gameplay-entropy.md)
  (rolls), [ADR-005](ADR-005-data-driven-sprite-animation-and-combat-vfx-pipeline.md) (vector icons)

## Context

The owner asked for more effects (being hit, poisoned, slowed, ...), shown as icons in the top-right corner, with a
details panel on click.

What exists on `master`:

- **Engine scaffold, unused.** `engine/Carbon.Combat/Statuses` has `StatusEffectBuffer` (a per-entity attachment with a
  fixed array of `ActiveStatusEffect`), `StatusEffectSystem` (ticks the buffers, emits `StatusPulseEvent`, applies
  damage or healing, writes `CombatStateComponent`) and `CrowdControlMask`. Only engine tests use them; the server never
  registers the system or creates a buffer. The buffer has one stacking rule (add a stack, keep the longer duration),
  and the system hard-codes a 0.5 speed for any slow (`StatusEffectSystem.cs:23`). There are no definitions, no config
  and no ids.
- **Food poisoning is a needs flag.** `EatActionSystem` sets `NeedsComponent.PoisonRemainingSeconds`
  (`engine/Carbon.Survival/Systems/EatActionSystem.cs:243`); `NeedsSystem` applies its mood, speed and regen penalties
  (`NeedsSystem.cs:243,290,297`). It deals no damage, it is persisted as `needs_poison_seconds` (ADR-015 D1) and the client
  only sees `PlayerStatsSync.FoodPoisoned` (a hunger-bar tint).
- **Hunger, malnutrition and rest** are needs states (`HungerState`, `RestState`) that already change speed, mood and
  regen. Clients see them as bars, not as effects.
- **Combat hits** are `CombatHit` broadcasts (damage, remaining HP, crit, kill). Monster hits on players go through
  `AuthoritativeWorld.ApplyDamageToPlayerLocked` (`AuthoritativeWorld.TickAndAoi.cs:301`) for melee, leaps and
  projectiles. A hit has no secondary effect: no monster can poison, slow, bleed or stun.
- **Movement** speed is `walk/sprint × stats × needs` (`AuthoritativeWorld.TickAndAoi.cs:64`); nothing else scales it.
- **No buffs** exist.

Gaps: no data-driven definitions, no stacking rules, no server integration or replication, no monster procs, no
heavy-hit reaction, no buffs, food poisoning outside any effect system, no client display beyond the hunger tint.

## Decision

1. **D1 — Definitions in `resources/config/status_effects.json`.** A new ADR-014 config document, hot-reloadable,
   embedded in `Carbon.Combat` as the shipped default. Each effect has:
   - `id` (stable `ushort`, sent on the wire), `key` (localization and persistence key), `icon` (a `.cvg.json` id);
   - `category` (`Debuff`, `Buff`), optional `derived` (`Hungry`, `Starving`, `Tired`, `WellFed`);
   - `durationSeconds`, `stacking` (`Refresh`, `Stack`, `Strongest`), `maxStacks`;
   - `tickIntervalSeconds` and `magnitude` (damage per pulse per stack), `lethal`;
   - `moveSpeedMultiplier`, `stun`, `healthRegenMultiplier`, `staminaRegenMultiplier`, `persist`.

   The document also has `heavyHit` (effect and minimum damage fraction of max HP), `restedOnWake` (effect and minimum
   rest fraction) and `monsterProcs` (monster id, effect, chance). `StatusEffectCatalog.LoadFromJson` validates ranges,
   unique ids and keys, and that every referenced effect exists. `ServerGameData.Load` also rejects a proc that names a
   monster not in `monsters.json`.
2. **D2 — Shipped effects.**

   | Key | Kind | Values |
   | :--- | :--- | :--- |
   | `poisoned` | debuff, Stack ×3 | 10 s, 1 damage per 2 s per stack, not lethal (stops at 1 HP), persisted |
   | `slowed` | debuff, Strongest | 3 s, move speed ×0.6 |
   | `bleeding` | debuff, Stack ×5 | 6 s, 0.75 damage per 1 s per stack, lethal, persisted |
   | `stunned` | debuff, Refresh | 0.5 s, no movement and no attacks |
   | `hungry` | derived | stacks 1–2 = Hungry, UrgentlyHungry |
   | `starving` | derived | Starving |
   | `tired` | derived | stacks 1–3 = Tired, VeryTired, Exhausted |
   | `well_fed` | derived buff | food at or above `needs.fullThreshold`, health regen ×1.5 |
   | `rested` | buff, Refresh | 600 s after waking from sleep with rest ≥ 95%, stamina regen ×1.25, persisted |

   Night terror and other ideas are out of scope.
3. **D3 — Stacking rules** in `StatusEffectBuffer.Apply`:
   - `Refresh`: one stack; duration resets to the new duration; magnitude is replaced.
   - `Stack`: one more stack up to `maxStacks`; duration resets to the longer of the two; the pulse timer keeps running.
   - `Strongest`: the incoming effect replaces the active one only if it is stronger
     (`magnitude + (1 − moveSpeedMultiplier)`); an equal one extends the duration; a weaker one is ignored.

   Each change that the client must see (add, stack, refresh, replace, expire, remove) bumps `StatusEffectBuffer.Version`.
   A pulse does not.
4. **D4 — Tick and storage.** Each player gets one `StatusEffectBuffer` attachment at world entry (a fixed array of
   unmanaged `ActiveStatusEffect` structs, never reallocated past 8 entries). The server registers `StatusEffectSystem`
   in the simulation, so effects tick inside the ADR-017 world tick after commands and movement input. Durations and
   intervals are converted to ticks on apply. Every effect value (magnitude, multipliers, flags) is copied into the
   active entry, so a hot reload never changes an effect that is already running. The steady-state tick allocates
   nothing: no LINQ, no closures, scratch lists reused (`ServerTickAllocationTests` budget unchanged).
5. **D5 — Gameplay.**
   - Move speed multiplies by the product of the active `moveSpeedMultiplier`s; `stun` makes it 0
     (`CombatStateComponent.MoveSpeedScale`). The old fixed 0.5 slow is removed.
   - A stunned player's attack input is dropped.
   - Damage pulses go through `StatusEffectSystem`. A non-lethal effect never takes the last hit point. For players the
     server sends each pulse as a `CombatHit` tagged with the effect id, and a lethal pulse uses the normal death and
     respawn path. Death clears every effect.
   - `healthRegenMultiplier` scales out-of-combat regen; `staminaRegenMultiplier` scales stamina regen.
6. **D6 — Needs poison is folded into `poisoned`.** The `poisoned` effect is the single source of truth. A food poisoning
   roll (`MealCompletedEvent.Poisoned`) applies `poisoned` with source `Food` and duration `needs.poisonDurationSeconds`.
   Every tick, the server mirrors the effect's remaining time into `NeedsComponent.PoisonRemainingSeconds`, so the needs
   penalties (`poisonMood`, `poisonSpeedScale`, regen block in `needs.json`) apply to every poison source. A monster
   poison is therefore food poisoning plus damage. Food poisoning now also deals the `poisoned` damage, but cannot kill.
7. **D7 — Monster procs and heavy hits.** After a monster hit lands on a player (melee, leap or projectile; not dodged),
   the server:
   - applies `heavyHit.effect` when the mitigated damage is at least `minDamageFraction` of the victim's max HP;
   - rolls each proc of the attacker's monster type with `StatusProcRoll.Sample(KeyAt(tick), tick, victim, attacker,
     procIndex)` on the new `GameplayDomain.StatusProc` stream (ADR-026). The server secret keys the roll, never the
     world seed.

   Shipped procs: `wild_slime` and `slime_king` slow, `frost_wolf` bleeds and slows, `ash_wraith` poisons,
   `orc_warlord` and `crimson_demon` bleed.
8. **D8 — Persistence.** Migration `0010_character_status_effects` adds `status_effects TEXT NOT NULL DEFAULT ''` in
   SQLite and PostgreSQL, and `CharacterRecord` gains a trailing `StatusEffects = ""`. Only effects with `persist: true`
   are saved, as `key:remainingMs:stacks` entries joined by `;`. Keys, not ids, so a config edit cannot load the wrong
   effect. On entry, unknown keys and malformed entries are skipped, and durations are clamped to the current
   definition (for `poisoned`, at least `needs.poisonDurationSeconds`). A row with no saved effects but
   `needs_poison_seconds > 0` (saved before this ADR) restores `poisoned` from that column. `needs_poison_seconds` is
   still written as the poison mirror. Stun, slow and derived effects are not saved: they are short or recomputed.
9. **D9 — Replication to the owner only.** New S2C packet `StatusEffectSync` (opcode `0x4058`; `0x4058–0x405F` are
   reserved for status effects inside the Items band). It carries parallel arrays: effect id, stacks, remaining ms
   (0 = while the condition holds), full duration ms, magnitude, source kind and source id (monster archetype). It is
   sent to the owning connection only, at world entry and on the tick when the buffer version or the derived set
   changes. The client counts down locally, so the steady state sends nothing. The server sends ids and numbers,
   never text. `CombatHit` gains a trailing `StatusEffectId` (0 for a direct hit), so the client can tint damage numbers.
10. **D10 — Client.** The desktop client reads `status_effects.json` from the synced config (icons, categories, rules).
    It adds a top-right status strip (one icon per effect, duration ring, stack count; hover tooltip; click opens a
    details panel with localized name, description, remaining time, source and magnitude; click outside or Esc closes)
    and hit feedback (red vignette on damage, damage-number tint per effect, green tint while poisoned, slow trail).
    Icons are vector `.cvg.json` files. Strings live in a new `[status_effects]` section of `en.ini` and `vi.ini`.
11. **D11 — Monsters.** Monsters get no status effects in this ADR. Applying effects to monsters needs AI and replication
    work that no request needs yet.

## Alternatives Considered

- **A new effect system next to the engine scaffold.** Rejected. `StatusEffectBuffer` and `StatusEffectSystem` already
  have the right shape (struct entries, no allocation, pulse events, crowd-control state). Extending them avoids two
  systems for one idea.
- **Keep food poisoning in `NeedsComponent` and show it as a fake effect.** Rejected. Two sources of truth for "poisoned"
  would drift, and monster poison would not get the needs penalties.
- **Move the poison penalties from `needs.json` into `status_effects.json`.** Rejected for now. It changes the shipped
  needs config and its tests for no gameplay gain. The mirror in D6 keeps one effect with one set of penalties.
- **Procs in `monsters.json`.** Rejected. `monsters.json` needs a server restart, and its schema is shared with the
  spawner. Keeping procs in `status_effects.json` keeps every effect value in one hot-reloadable document.
- **Replicate effects every tick, or in `PlayerStatsSync`.** Rejected. Per-tick sends waste bandwidth, and the stats
  sync is sent every 10 ticks with its own arrays. A change-only packet is smaller and delta-friendly.
- **Persist effect ids or a binary blob.** Rejected. Ids are stable but a typo in a hand edit can silently map to another
  effect; keys fail safe. A blob hides the values from SQL (as in ADR-012 and ADR-015).
- **Broadcast effects to observers in AoI.** Rejected for now. Only the owner needs the strip; other players' visual cues
  can be added later on the same ids.

## Consequences

- Monsters can poison, slow, bleed and stun; players see every effect with its remaining time, and relogging does not
  clear poison or bleeding.
- Food poisoning now deals small damage. It cannot kill, and its mood, speed and regen penalties are unchanged.
- One new config document, one packet, one appended `CombatHit` field, one migration and one appended record member.
  All are additive: older rows load with no effects, and old clients ignore the trailing field.
- `StatusEffectBuffer.Version` makes replication change-driven. A future AoI broadcast or monster effects can reuse it.
- Trade-off: the poison mirror (D6) is one write per player per tick. It goes away if the needs penalties ever move
  into `status_effects.json`.

# ADR-036: Status Effect Levels and Monster Effect Abilities

- **Date**: 2026-11-27
- **Status**: Accepted
- **Amends**: [ADR-029](ADR-029-server-authoritative-status-effects.md) (D1 definitions, D3 stacking, D7 procs, D8 persistence,
  D9 replication, D10 client)
- **Scope**: `engine/Carbon.Combat/Statuses`, `resources/config/status_effects.json`, `contracts/Carbon.Contracts.Common`,
  `contracts/Carbon.Contracts.Entities`, `contracts/Carbon.Contracts.Items`, `server/Carbon.Abstractions`,
  `server/Carbon.Application/World`, `server/Carbon.Host`, `desktop/`, `resources/localization`, `resources/art/ui`, `tests/`
- **Builds on**: [ADR-026](ADR-026-server-secret-gameplay-entropy.md) (proc rolls), [ADR-032](ADR-032-water-depth-tiers-swimming-diving-and-underwater-life.md)
  (water depth), [ADR-033](ADR-033-hardcore-stamina.md) (stamina regen)

## Context

The owner asked for effect levels ("1–4–n, the higher the stronger, stored as a byte, like Minecraft") and for more
monsters that apply effects.

What ADR-029 shipped on `master`:

- **No level.** An effect is one definition with one value set: `poisoned` always deals 1 per pulse per stack, `slowed`
  is always ×0.6 (`resources/config/status_effects.json`). `ActiveStatusEffect` has stacks but no strength axis
  (`engine/Carbon.Combat/Statuses/ActiveStatusEffect.cs`), and `Strongest` compares a derived `Strength`
  (`magnitude + 1 − moveSpeedMultiplier`).
- **Wire.** `StatusEffectSync` (`0x4058`) has seven parallel arrays; `CombatHit` ends with `StatusEffectId`. The
  protocol version is 2 (`contracts/Carbon.Contracts.Common/CarbonProtocol.cs`).
- **Persistence.** `status_effects` holds `key:remainingMs:stacks;...` (ADR-029 D8, migration 0010).
- **Procs.** Seven procs on five monsters, each a fixed effect at its single strength
  (`StatusEffectService.OnMonsterHit`). Every proc rolls on `GameplayDomain.StatusProc`.
- **Monster strength.** A monster's level already grows with distance from the world origin
  (`engine/Carbon.Creatures/Scaling/MonsterScaling.cs`), and night is a live flag (`MonsterSpawnerSystem.IsNight`) that
  AI already reads per tick (`AuthoritativeWorld.TickAndAoi.cs:92`).
- **Roster.** `monsters.json` has nine monsters: `wild_slime`, `slime_king`, `rock_golem`, `frost_wolf`, `ash_wraith`,
  `skeleton_archer`, `orc_shaman`, `orc_warlord`, `crimson_demon`. There is no spider and no bat.

## Decision

1. **D1 — Level is a byte, 1..255.** `ActiveStatusEffect.Level`, `StatusPulseEvent.Level` and every wire and persisted
   copy are a `byte`; 1 is level I. 0 is not a level: on apply it becomes I, and in a saved row it is skipped. Each
   definition gains `maxLevel` (1..255, default 1) and linear per-level steps added for each level above I:
   - `magnitudePerLevel`: damage per pulse per stack (poison, bleeding, burning, frostbite);
   - `moveSpeedMultiplierPerLevel`: usually negative for a slow; the result is clamped to
     `StatusEffectDefinition.MinMoveSpeedMultiplier` (0.05), so no level ever stops a player;
   - `healthRegenMultiplierPerLevel`, `staminaRegenMultiplierPerLevel`: regen buffs and debuffs, clamped to 0..10.

   The level is clamped to `maxLevel` and resolved into the active entry's values on apply (ADR-029 D4 still holds: a
   hot reload never changes a running effect). A definition with a per-level step and `maxLevel` 1 is rejected.
2. **D2 — Shipped levels.**

   | Key | Max | Per level | I / II / III |
   | :--- | :---: | :--- | :--- |
   | `poisoned` | V | +1 damage per pulse per stack | 1 / 2 / 3 per 2 s |
   | `bleeding` | V | +0.5 damage per pulse per stack | 0.75 / 1.25 / 1.75 per 1 s |
   | `slowed` | V | −0.1 speed multiplier | ×0.6 / ×0.5 / ×0.4 (V = ×0.2) |

   Other effects stay at I. Derived effects (`hungry`, `starving`, `tired`, `well_fed`) are always level I: their
   severity is still the stack count, and no source grants a stronger `well_fed` yet. The regen steps exist for timed buffs
   such as a future potion.
3. **D3 — Level and stacks are separate axes.** Stacks count applications; level measures the source's strength. Merges
   in `StatusEffectBuffer.ApplyOrRefresh`:
   - `Strongest`: a higher level replaces the active entry (full new duration); a lower level is ignored, so it never
     shortens or refreshes a higher one; at the same level the ADR-029 strength rule still applies.
   - `Refresh`: the same level resets the duration; a higher level upgrades and resets it; a lower level is ignored.
   - `Stack`: every application adds one stack up to `maxStacks`, and the duration becomes the longer of the two. The
     entry keeps the higher level and that level's values. Why: if a weaker hit downgraded the entry, a level-1 spider
     could weaken a level-3 poison by biting; if an upgrade reset the stacks to 1, a stronger hit could lower the damage
     (3 stacks × I = 3 per pulse, but 1 stack × II = 2). With this rule, damage per pulse never drops when a hit lands.
4. **D4 — Wire.** `StatusEffectSync` gains `[SerializeOrder(7)] byte[] Levels`, and `CombatHit` gains
   `[SerializeOrder(7)] byte StatusEffectLevel` (0 for a direct hit, so the client can show "Poison II" on a pulse
   number). Both are appended; no field is renumbered. `CarbonProtocol.Version` goes from 2 to 3, so an old client is
   refused at login and never reads a short array. No new opcode.
5. **D5 — Persistence: format version 2 in the existing column.** No migration (0019 is not used). A saved value starts
   with the token `v2`, then `key:remainingMs:stacks:level` entries, all joined by `;`
   (`v2;poisoned:10000:2:3`). A value without the token is the ADR-029 format and loads at level I. In a `v2` value, an
   entry without four fields or with level 0 is skipped, and a level above `maxLevel` is clamped. The
   `CARBON_DEBUG_STATUS_EFFECTS` review knob accepts both formats.
6. **D6 — Client.** The level is shown as a Roman numeral I to X (Arabic digits from 11 up, as Minecraft does) on the
   icon (top left; the stack count keeps the top right and the countdown the bottom left) and in the details panel. Level I shows no numeral on the icon and the bare
   name ("Poisoned", not "Poisoned I"), also as in Minecraft. The panel title and the hover tooltip read
   "Poisoned II". The numerals and the name-with-level format are keys in a new `[status_levels]` section of `en.ini` and
   `vi.ini`; there are no literals in code.
7. **D7 — Monster effect abilities in config.** A `monsterProcs` entry gains optional `level` (base level, default 1),
   `durationSeconds` (override, default the effect's duration) and `minDamageFraction` (the proc can fire only on a hit
   that deals at least that share of the victim's max HP, default 0, for "stun on a heavy hit"). Every monster gets at
   least one proc:

   | Monster | Archetype | Procs (chance, base level) |
   | :--- | :--- | :--- |
   | `wild_slime` | slime | slowed 35 % I |
   | `slime_king` | high-tier slime | slowed 50 % II |
   | `rock_golem` | brute | stunned 100 % I on a hit ≥ 12 % of max HP |
   | `frost_wolf` | wolf, frost | bleeding 30 % I, slowed 20 % I, frostbite 25 % I |
   | `ash_wraith` | fire caster | burning 30 % I, blindness 15 % I |
   | `skeleton_archer` | ranged | poisoned 25 % I (poison-tipped arrows) |
   | `orc_shaman` | shaman | weakness 35 % I |
   | `orc_warlord` | orc elite | bleeding 25 % I, stunned 100 % I on a hit ≥ 15 % of max HP |
   | `crimson_demon` | demon | bleeding 30 % I, burning 25 % II |

   The roster has no spider and no bat. Adding species (art, spawn tables, loot, names) is a content task outside this
   ADR; their effects would only be config rows. On-hit auras are not added: no monster needs one yet.
8. **D8 — The effect level scales with monster level and night.** In `status_effects.json`:

   ```json
   "procLevelScaling": { "monsterLevelsPerEffectLevel": 15, "nightBonusLevels": 1 }
   ```

   `effectLevel = clamp(proc.level + floor((monsterLevel − 1) / monsterLevelsPerEffectLevel) + (night ? nightBonusLevels : 0), 1, effect.maxLevel)`.

   - Monster level already grows with distance from the world origin, so "farther from spawn" is this term; adding a
     distance term too would count it twice.
   - Night is read when the hit lands, from the same flag the AI uses for night aggro, not when the monster spawned: a
     player at night feels the night in every hit, whatever the monster's spawn time.
   - Example: a level-30 monster with a base-I poison applies II by day (1 + 1) and III at night (1 + 1 + 1). A level-1
     slime applies slow I by day and II at night.
   - The roll itself is unchanged: `StatusProcRoll.Sample` on the secret `GameplayDomain.StatusProc` stream (ADR-026). The
     level adds no randomness.
9. **D9 — New effects.** Each has config, a vector icon (`resources/art/ui/status_<key>.cvg.json`), `en` and `vi` text, and
   a server mechanic:

   | Key | Id | Rule | Values | Mechanic |
   | :--- | :---: | :--- | :--- | :--- |
   | `weakness` | 11 | Strongest, max III | 8 s, damage dealt ×0.75, −0.1 per level | the player's melee, skill and projectile damage is multiplied before mitigation |
   | `blindness` | 12 | Strongest, max III | 5 s, view ×0.5, −0.1 per level | the server shrinks that player's AoI enter and exit radii, so distant entities leave the client; the client also darkens the screen edge |
   | `burning` | 13 | Stack ×3, max V | 5 s, 1.5 per 1 s per stack, +1 per level, lethal | entering water at least `Shallow` deep (ADR-032) puts it out |
   | `frostbite` | 14 | Refresh, max III | 6 s, 0.5 per 2 s, +0.5 per level, not lethal; stamina regen ×0.6, −0.1 per level | damage pulses and slower stamina regen |

   New definition keys: `damageDealtMultiplier` (+ `PerLevel`), `viewRadiusMultiplier` (+ `PerLevel`, clamped to at least
   0.25 so a player always sees nearby enemies), and `extinguishedByWater`.
10. **D10 — Telegraphs hint at the effect.** On the client, a monster's attack telegraph (melee wedge, aim line and
    reticle, spell zone, leap zone) takes the colour of its first proc's effect from the synced `status_effects.json`
    (`StatusEffectTints.ForTelegraph`): green for poison, orange for burning, violet for weakness, and so on. The archetype
    is resolved once per catalog (`MonsterEffectTints`). Projectiles keep their colour: a projectile entity does not carry
    its shooter's archetype, and the aim line already shows the colour before the shot. Blindness also darkens the screen
    edge in proportion to the lost view, and frostbite uses the cold edge of a slow. Vector geometry only, no new sprites.

## Alternatives Considered

- **An explicit `levels` table per effect.** Rejected for now. A table needs up to 255 rows for an effect that can reach
  255, and none of the shipped effects needs a curve. A linear step is one number per value. A table can be added later
  as an override without changing the wire or the saved format.
- **Fold level into stacks (level = stack count).** Rejected. The owner wants a level that a stronger source gives
  directly; stacks already mean "hit again". Folding them would make a single strong hit look like three weak ones.
- **Stack rule: an upgrade resets stacks.** Rejected (D3): it can lower damage per pulse on a stronger hit.
- **A migration with a `level` column, or a new column.** Rejected. Effects live in one text column (ADR-029 D8); a
  version token keeps old rows readable and needs no schema change on either store.
- **Night from spawn time.** Rejected (D8): a monster spawned at dusk would hit at day strength all night.
- **Blindness as a client-only darker screen.** Rejected: a modified client could ignore it. Shrinking the AoI is
  authoritative; the vignette is only feedback.
- **New spider and bat monsters now.** Deferred (D7): it needs art, spawn and loot content, not effect rules.

## Consequences

- One effect can range from mild to deadly; a far or night monster is visibly more dangerous ("Poison III").
- Every monster type applies at least one effect, all from hot-reloadable config.
- Wire: one appended array and one appended byte, with a protocol bump to 3. Old clients must update.
- Persistence: no migration; rows saved before this ADR load at level I.
- Blindness changes the AoI radius per player; the tracker keeps one shared scratch, so the per-tick cost is unchanged.
- Trade-off: linear steps cannot express a curved scale; an override table would be the follow-up.

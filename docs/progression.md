# Character Progression: Attributes, Derived Stats & the XP Curve

How a character grows: experience per level, attribute points per level, what each attribute does, how crits, defense,
magic resist and evasion work in combat, and what a respec costs. All numbers live in
[`resources/config/progression.json`](../resources/config/progression.json) (ADR-014 config sync,
[ADR-021](adr/ADR-021-data-driven-character-progression.md)). The code is in `engine/Carbon.Items/Progression`.

## 1. Evaluation of the previous system

Measured against the shipped configs before this change (`CharacterProgressionPolicy`, `ComputeExperienceReward`,
`monsters.json`, `spawning.json`, `economy.json`).

| # | Finding | Evidence | Effect |
| :--- | :--- | :--- | :--- |
| F1 | **XP curve far too flat.** `xpToNext = 100 + 75 × (level − 1)`, linear. Level 1→99 needs 366,275 XP in total. | `CharacterProgressionPolicy.GetXpToNextLevel` | With today's kill XP and the kill rate below (§5), level 1→10 takes **~12 minutes** and level 99 takes **~2.6 hours** of play. Kill XP grows with monster HP (×40 by level 80) but the curve grows only ×75. |
| F2 | **Flat points, no rhythm.** 4 attribute points every level, no milestones. | `StatPointsPerLevel = 4` | Late levels (hours each, after F1 is fixed) feel the same as level 2. |
| F3 | **Linear, uncapped attributes.** Power gives +3.5% attack per point with no cap: an all-Power level-99 character has +1,372% attack. Agility was the only capped stat (move speed 1.24). | `ComputeStats` | One dominant build; content tuning breaks at high levels. |
| F4 | **Crits ignore the character.** Crit chance comes only from the weapon; crit damage is a hard-coded ×1.5. | `PlayerActionService` (`* 1.5f`) | No stat for crit; DEX-style builds impossible. |
| F5 | **Defense ignores the attacker's level and has no cap.** Damage × `100 / (100 + 4 × def)`. A level-99 Vitality build reaches ~287 defense = 92% reduction against every monster. | `MitigateDamage` | Tank builds become immune at high levels; early defense is nearly worthless against later content but still huge in starter zones. |
| F6 | **No magic.** All monster damage is physical; there is no magic resist. Staffs scale with physical attack. | `ApplyDamageToPlayerLocked`, `PlayerActionService` | Wisdom is a mana battery, not a magic stat. Casters (ash wraith, orc shaman) are the same as brawlers. |
| F7 | **No evasion or attack speed stat** for players. Attack cooldown is a fixed 0.5 s. | `StatType` has `AttackSpeed`, players never use it | Agility only gives stamina and move speed. |
| F8 | **Regen does not scale.** Out-of-combat health regen is a flat +3 HP/s; a level-50 character with 2,000 HP needs 11 minutes to heal. MP regen is hard-coded in the tick. | `AuthoritativeWorld.TickAndAoi` | Downtime dominates high-level play. |
| F9 | **No level-difference XP rule.** A level-60 player farming level-1 slimes still gets full XP. The gold faucet already has a gray-monster rule (`economy.md` §2.4). | `ComputeExperienceReward` | XP and gold disagree; low-level farming is safe and efficient. |
| F10 | **Balance tables are compiled code** (ADR-014 debt), and the desktop recomputes unspent points with its own copy of the formula. | `CharacterProgressionPolicy`, `ServerAuthorityTests` allowlist | A balance change needs a client release and can show wrong numbers. |
| F11 | **No respec.** A misallocated point is permanent. | — | Players are punished for experimenting; no gold sink tied to progression. |

## 2. Attributes

Five allocatable attributes. Ids are persisted in the progression blob, so the first four keep their ids and names;
Dexterity is new (id 4). Per-point effects (`attributes` in `progression.json`):

| Attribute | Role | Per point |
| :--- | :--- | :--- |
| **Vitality** (VIT) | Tank | +14 max HP, +0.7 defense, +0.15 HP/s regen |
| **Wisdom** (WIS) | Caster | +10 max MP, +1.2% spell power (magic attack), +0.6 magic resist, +0.3 MP/s |
| **Power** (POW) | Physical damage | +1.2% attack, +2 stamina |
| **Agility** (AGI) | Tempo & avoidance | +0.25% attack speed, +0.2% move speed, +1 evasion rating, +6 stamina |
| **Dexterity** (DEX) | Precision | +0.2% crit chance, +0.35% crit damage, +0.3% attack |

Why five: each attribute owns one combat question (survive, cast, hit hard, act often/avoid, hit precisely), every
derived stat the owner asked for has exactly one primary source, and DEX gives crit builds a home without overloading
Agility. Power and Dexterity both add attack so a crit build is not a pure gamble; Dexterity's attack share is a
quarter of Power's.

Every value is ≥ 0, so a stat never drops when a level or a point is added (tested for every level and every
attribute).

### Derived stats

`stat = base + perLevel × (level − 1) + Σ attribute points × per-point effect + gear`, then caps.

| Stat | Base (Lv1) | Per level | Cap | Used for |
| :--- | ---: | ---: | :--- | :--- |
| Max HP | 100 | +18 | — | survival |
| Max MP | 80 | +8 | — | skills, staff shots |
| Max stamina | 100 | +5 | — | sprint, skills |
| Defense | 2 | +1 | 75% reduction | physical hits (§4.2) |
| Magic resist | 1 | +0.6 | 75% reduction | magical hits; armor also adds 50% of its defense |
| Attack | ×1.00 | +3% | — | melee and bow damage |
| Spell power | ×1.00 | +3% | — | staff bolts and spells |
| Crit chance | 0% + weapon | — | **50%** total | §4.1 |
| Crit damage | ×1.50 | — | **×2.50** | §4.1 |
| Attack speed | ×1.00 | — | **×1.50** | divides the 0.5 s attack cooldown |
| Move speed | ×1.00 | — | **×1.25** | walk and sprint speed |
| Evasion rating | 0 | — | 30% dodge | §4.4 |
| HP regen | 3/s | +0.25/s | — | plus 1% of max HP per second, out of combat (6 s without a hit) |
| MP regen | 6/s | +0.1/s | — | always |

Gear still comes from `items.json` (armor and accessory slots 36–41: defense, bonus HP/MP; held weapon crit chance) with the enhancement
multiplier from `ItemEnhancementPolicy` (unchanged).

## 3. Attribute points per level

| Levels | Points per level-up | Milestones |
| :--- | ---: | :--- |
| 2–20 | 4 | +5 extra on every 10th level (10, 20, …, 90) |
| 21–50 | 5 | |
| 51–80 | 6 | |
| 81–99 | 7 | |

Totals: 86 points at level 20, 251 at 50, 446 at 80, 584 at 99 (the old rule gave 76, 196, 316, 392).

Why this shape:

- **Levels get longer, so each one should be worth more.** Level 2 takes 3 minutes; level 98 takes over 2 hours (§5).
  A flat 4 points makes late level-ups feel empty. Points grow ×1.75 while time per level grows ×45, so the growth is
  felt without inflating stats (monster HP grows ×17 by level 80; attributes add linearly).
- **Milestones match the world.** Every 10 levels is roughly when a player moves to a harder band in `spawning.json`
  and changes gear tier. The +5 burst is a visible reward at that moment.
- **No existing character goes over budget.** Every level grants at least as many points as the old 4-per-level rule
  (tested), so saved allocations stay valid without a reset. If a later config change lowers the budget, the server
  refunds the whole allocation on load (§6).

## 4. Combat math

One damage rule for every path (audit DOM-1): engine systems, player hits on monsters and monster hits on players all
resolve a hit through `CombatDamage.TryResolve`, which refuses hits on invulnerable targets and applies a mitigation
policy: `ArmorDamageMitigation` (creature armor, `100 / (100 + armor)`) or `ProgressionDamageMitigation` (the player
rule below). `DamageMitigationTests` fails if the formulas are copied elsewhere.

All rolls are deterministic: `CombatRoll.Roll(chance, nonce, salt)` hashes a per-character counter, so replays and
tests are reproducible and the client cannot influence the outcome.

### 4.1 Critical hits

`critChance = min(50%, weapon crit + DEX bonus)`; a crit multiplies damage by `min(×2.5, 1.5 + DEX bonus)`.
Melee crits still require an optimal swing (a matching tool or weapon). An item at 0 durability no longer exists ([ADR-035](adr/ADR-035-items-break-at-zero-durability.md)).

### 4.2 Defense and magic resist (diminishing returns)

```
reduction = min(cap, rating / (rating + K + perAttackerLevel × attackerLevel))
damage    = max(1, raw × (1 − reduction))
```

With `K = 50`, `perAttackerLevel = 5`, `cap = 75%`. The attacker's level is the monster's level, so the same defense
is worth less against harder monsters: 100 defense gives 50% against a level-10 monster and 20% against a level-70 one.
Each extra point is worth less than the previous one, and the cap means no build becomes immune. Typical balanced
characters sit at 25–40% reduction against monsters of their own level.

### 4.3 Physical vs magical damage

A monster with any `Spell` attack is a caster (ash wraith, orc shaman). Its `Spell` and `Projectile` attacks are
magical; everything else (melee, leaps, arrows) is physical. Magical hits use magic resist instead of defense. Staffs
(`MagicFocus`) scale with spell power instead of attack.

### 4.4 Evasion

`dodge = min(30%, evasion / (evasion + 100 + 2 × attackerLevel))`. A dodged hit deals 0 damage (shown as a miss).
Evasion is a separate roll from crits (different salt).

## 5. Experience curve and time-to-level targets

### Kill XP

```
base  = clamp(0.45 × monster max HP + 4 × monster level, 25, 4000) × 2 (elite) × 1.35 (night)
xp    = base × levelDifference(monsterLevel − playerLevel)
```

`levelDifference`: 1.0 within 5 levels below the player, −10% for each level beyond that (floor 10%), +5% per level
above the player (max +25%). This matches the economy's gray-monster rule (F9). The old 650 XP cap is raised to 4,000:
with the old cap, kill XP stopped growing at level ~65 while monster HP kept growing, so the curve had to flatten.

### XP to next level

`experience.xpToNext` anchors, geometrically interpolated between anchors (smooth, strictly increasing):

| Level | 1 | 5 | 10 | 15 | 20 | 25 | 30 | 40 | 50 | 60 | 70 | 80 | 90 | 98 |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| XP | 450 | 1,700 | 5,000 | 9,500 | 16,000 | 28,000 | 44,000 | 84,000 | 135k | 220k | 315k | 470k | 700k | 870k |

### Play model (the simulation)

`TimeToLevelSimulation` (`tests/engine/Carbon.Items.Tests`) plays an engaged melee player against normal monsters of
their own level, using the real tables:

- **Monsters:** every non-elite `monsters.json` entry that spawns at that level, scaled by `spawning.json` stat curves;
  night spawns ×1.3 stats and ×1.35 XP for 10 of 24 hours (`night.startHour`/`endHour`).
- **Density:** 3 monsters per 1280×720 screen by day, 5 at night (PR #67), so the walk to the next monster is the
  mean nearest-neighbour distance `0.5 / √density` at 250 u/s × move speed.
- **Player:** 2 swings/s × attack speed, gear from `items.json` by level (copper sword + leather → iron → demon
  cleaver → ember blade, enhanced +1 every 8 levels), points split by build. Monster damage per second comes from its
  attack profiles (`max(cooldown, duration)` per attack), mitigated as in §4.
- **Per kill:** time to kill + walk + 6 s overhead (approach, loot, dodging telegraphs) + rest time to regenerate the
  damage taken. Players fight 60% of their play time (selling, crafting, repairs, travel take the rest).

This gives 90–180 kills per hour, consistent with the economy simulator's 120 kills/hour (`economy.md` §4), so XP and
gold pacing agree.

### Targets and results (balanced build: 25% VIT, 35% POW, 15% AGI, 25% DEX)

| Level → next | Target | Simulated | Kills/h | Time to kill | HP lost per kill |
| :--- | ---: | ---: | ---: | ---: | ---: |
| 1 → 2 | ~3 min | 3.1 min | 182 | 1.4 s | 9% |
| 9 → 10 | ~10 min | 10.4 min | 148 | 2.4 s | 10% |
| 19 → 20 | ~17 min | 18.5 min | 121 | 3.5 s | 12% |
| 29 → 30 | ~27 min | 27.8 min | 153 | 2.3 s | 8% |
| 49 → 50 | ~49 min | 46.2 min | 114 | 3.5 s | 14% |
| 79 → 80 | ~93 min | 89.4 min | 90 | 5.0 s | 19% |
| 98 → 99 | ~2.5 h | 2.3 h | 94 | 4.8 s | 18% |

| Bracket | Target | Simulated |
| :--- | ---: | ---: |
| 1 → 10 | ~1 h | 1.0 h |
| 10 → 20 | ~2.25 h | 2.3 h |
| 20 → 30 | ~3.75 h | 3.9 h |
| 30 → 50 | ~13 h | 12.9 h |
| 50 → 80 | ~37 h | 33.2 h |
| 80 → 99 | ~38 h | 35.1 h |
| **1 → 99** | **~100 h** | **88.5 h** |

Other builds, minutes per level relative to balanced: tank (60% VIT) 0.95–1.16×; skirmisher (40% AGI) 1.0–1.05×;
pure offence (60% POW, 40% DEX, no VIT) 1.1–1.6×, increasingly at high levels, because it kills faster but spends
the saved time resting. Every tested build stays within 0.6–1.7× of balanced at every tested level (tested).

The tests fail if any representative level leaves ±30% of its target, any bracket leaves ±20%, the whole curve leaves
80–120 hours, the kill rate leaves 60–240/h, a level-appropriate kill takes more than 8 s, or one fight costs half the
health bar.

## 6. Respec

Retired from the UI and the wire by [ADR-034](adr/ADR-034-retire-gold-attribute-reset.md): a respec-scroll item will
replace it. `CharacterProgressionService.TryResetAttributes` keeps the refund logic and price below for that item; no
packet reaches it today.

- Free through level 10 (players learn the system).
- Then `50 + 20 × level` gold: 450 g at level 20, 1,050 g at 50, 1,650 g at 80, 2,030 g at 99.
- That is about **40–47 minutes of net gold** at every stage (`economy.md` §7: net 635 g/h at 20, 1,353 at 50,
  2,340 at 75, 2,864 at 100), so it is a real decision and a progression-linked gold sink, but never a grind.
- The gold is debited through the wallet (`CurrencyReason` ledger) in the same tick as the reset; a player who cannot
  pay is rejected with no change.
- If a config change ever leaves a saved character over budget, the server refunds the allocation for free on load.

## 7. Wiring

| Side | What |
| :--- | :--- |
| Config | `progression.json` is loaded and validated by `ProgressionConfig.LoadFromJson` (fails fast at boot, rejected on a bad hot reload) and served to clients by the normal manifest (not server-only: clients need it for the panel preview). |
| Server | `CharacterProgressionService` (`server/Carbon.Application/World/Progression`) uses the config for level-ups, kill XP, point budget, derived stats, mitigation, evasion, crits and attack speed. Allocation and reset requests are tick-queued commands (ADR-017) and validated against the budget and the wallet. No DB migration: allocations already persist in the progression blob by attribute id. |
| Client | The Character tab (`[Y]`, `CharacterSheetModalWidget`) shows the five attributes with their per-point effects and + buttons (Shift+click spends 5), the unspent points, the 14 derived stats (DEF, MRES and evasion as a % against a same-level monster). Hovering a + previews in green every stat that point changes. `CharacterStatsPreview` computes it from the synced `progression.json` and `items.json` with `CharacterStatCalculator`, the same code the server runs. The server stays authoritative: the client never decides a point is affordable. A dodged monster hit shows "Miss". Screenshots: `tools/screenshots/capture.sh <dir> '^stats_panel'`. |

## 8. Tuning

| Want | Change | Check |
| :--- | :--- | :--- |
| Faster/slower leveling in one bracket | `experience.xpToNext` anchors in that range | `TimeToLevelSimulationTests` |
| More/less XP everywhere | `experience.kill.maxHealthFactor`, `levelFactor` | same; also re-check `economy.md` kill-rate assumptions |
| Points per level | `statPoints.brackets`, `milestoneBonus` | `ProgressionConfigTests` (never below 4/level for saved characters) |
| An attribute too strong/weak | its block in `attributes` | the build-parity test |
| Tankiness | `mitigation.physical/magical` `k`, `perAttackerLevel`, `cap` | HP-lost-per-kill assertions |
| Respec price | `respec` | net gold/hour in `economy.md` |

Edits hot-reload (ADR-014 D12): the server and clients switch tables together.

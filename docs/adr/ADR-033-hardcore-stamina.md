# ADR-033: Hardcore Stamina — Every Physical Action Costs

- **Date**: 2026-11-27
- **Status**: Accepted
- **Amended**: 2026-11-27 — playable tuning (see "Amendment: playable tuning")
- **Scope**: `resources/config/stamina.json`, `engine/Carbon.Survival/Stamina`, `contracts/Carbon.Contracts.Items`, `server/`, `desktop/`, `resources/localization/`, `tests/`

## Context

The owner asked for stamina to matter: chopping, mining, attacking and using tools must cost stamina, sprint should
cost a bit less, regen should be slow, and the whole thing should feel hardcore.

Stamina before this ADR (all on `master` at `7b7e627`):

- **Pool.** `DerivedCharacterStats.MaxStamina` from `progression.json`: `baseStats.maxStamina` 100, `perLevel` +5,
  `power` +2 and `agility` +6 per point. `VitalPools` (`server/Carbon.Application/World/Players/VitalPools.cs`) adds only
  the growth on level-up, and `CharacterProgressionService` clamps the current value to the new maximum. Stamina is
  persisted (`CharacterRecord.CurrentStamina`).
- **Sprint.** `AuthoritativeWorld.TickAndAoi.cs:58-62` drained a hardcoded **22/s** while moving with sprint held and
  stamina above 2.
- **Regen.** `AuthoritativeWorld.TickAndAoi.cs:74-77` regenerated a hardcoded **18/s** on every tick the player was not
  sprinting. There was no delay, no difference between standing and walking, and no link to rest or hunger.
- **Costs.** Only skills spent stamina (`SkillSpec.StaminaCost`, `PlayerActionService.UseSkill`). Melee swings, tool
  swings, bare-hand gathering, bow shots, the harvest key (`TryHarvestProp`), building and demolition were free.
  There is no dodge or roll.
- **At 0.** Nothing, except that sprint stopped below 2. Regen started on the next tick, so a player could pulse-sprint.
- **Needs (ADR-015, ADR-018).** Hunger and rest scale movement and attack speed and block HP regen, but never touched
  stamina. Food and potions with `RestoreStamina` add stamina directly. Respawn refills it.
- **Desktop.** `MultiplayerSession.cs:690-699` copied the 22/s and 18/s numbers to predict the bar, which breaks the
  ADR-014 rule that the desktop never hardcodes gameplay config.

## Decision

1. **D1 — One config document.** `resources/config/stamina.json` holds every number. It is a normal synced document
   (ADR-014): the server loads it at boot and on hot reload (`ServerGameData`), and clients receive it with the
   manifest. `ServerGameData` rejects `itemSwingCosts` entries that name an unknown item. The engine embeds the shipped
   copy as the default (`StaminaConfig.LoadShippedDefaults`). Fields:
   - `sprintDrainPerSecond`, `regenDelaySeconds`, `regenPerSecondStill`, `regenPerSecondMoving` (validated to be at most
     the still rate), `restingRegenMultiplier`, `exhaustedRecoverFraction`, `minimumRegenScale`.
   - `fatigueRegenScale` (`tired`, `veryTired`, `exhausted`) and `hungerRegenScale` (`hungry`, `urgentlyHungry`,
     `starving`).
   - `swingCosts` per swing kind (`bareHand`, `melee`, `tool`, `ranged`, `spell`) and `itemSwingCosts` per item code,
     which override the kind default. This is how cost scales by weapon and tool: heavier and higher-tier items cost
     more per swing but need fewer swings.
   - `buildWorkPerSecond` for construction and demolition work.
2. **D2 — One rule implementation.** `engine/Carbon.Survival/Stamina/StaminaMeter.cs` holds current stamina, the regen
   delay and the exhausted lock. The server owns the real meter on `PlayerState`. The desktop runs the same struct to
   predict between syncs.
   - `TrySpend(cost)` pays a discrete action, or refuses it.
   - `TryDrain(amount)` drains one tick of continuous effort (sprint, build work). It may take what is left.
   - `Regenerate(dt, max, rate)` first counts down the delay, then regenerates.
   - `StaminaConfig.GetRegenPerSecond` picks the still or moving rate, multiplies by the resting bonus, then by
     fatigue × hunger × status-effect scale, floored at `minimumRegenScale`.
3. **D3 — Refuse, don't weaken.** An action that costs more than the meter holds is **refused**: nothing happens (no
   damage, no wear, no cooldown, no projectile) and the body becomes **exhausted**. Reasons:
   - It is a single, cheap server check that the client can predict exactly. A "weak action" would need a second
     damage and wear table per tool.
   - A weak action still lets a player spam at 1 stamina. A refusal that exhausts closes that loophole.
   - The feedback is unambiguous: the bar flashes and the HUD says why.
4. **D4 — Exhaustion.** The body is exhausted when stamina reaches 0, or after a refused action.
   - While exhausted, every action with a stamina cost is refused, sprint is off, and build or demolition work pauses.
   - It ends when stamina is back to `exhaustedRecoverFraction` (35%) of max, so a player cannot pulse-act or pulse-sprint
     on a trickle.
   - Free actions stay available: walking, eating, consumables, placing kits, and staff casts (mana-paid,
     `swingCosts.spell` 0).
   - A refusal above 35% (for example, a 40-cost skill at 50) clears on the next tick. Exhaustion therefore only bites
     when the pool is actually low.
5. **D5 — What costs what (server).** Every check runs inside the world tick lock.
   - **Primary swing** (`PlayerActionService.Attack`): the cost is paid up front, hit or miss, before the cooldown starts.
     It comes from `StaminaConfig.GetSwingCost(held item)`. This covers melee strikes, tool swings at props and
     structures, bare-hand gathering and bow shots. Placeable kits and consumables are free.
   - **Harvest key** (`TryHarvestProp`): the same swing cost. A refusal answers `OpStatus.RateLimited`, the
     existing "retry later" status, so the wire enum is unchanged.
   - **Skills**: `SkillSpec.StaminaCost` now goes through the meter, so exhaustion blocks it and the spend delays regen.
   - **Sprint**: `sprintDrainPerSecond` per tick while moving with sprint held and the meter allows it. Regen skips
     sprinting ticks.
   - **Building and demolition** (`ProcessBuildJobsLocked`): `ConstructionService.StepJob` now returns whether work was
     applied. A job pauses while the builder is exhausted or empty, and work that progressed drains
     `buildWorkPerSecond`.
   - **Regen**: each non-sprinting tick. It is "moving" when the body's velocity is above 1 px/s (the same test needs
     activity uses), and "resting" while sitting or sleeping (ADR-018). Fatigue and hunger come from the player's
     `NeedsComponent`.
   - **Death (ADR-031)**: a dead player neither spends nor regenerates stamina; inputs are not counted as refusals.
     **Respawn** refills the meter and clears exhaustion.
6. **D6 — Wire.** `StaminaStatusSync` (S2C, `ItemOpCode.StaminaStatusSync = 0x4088`), allocated from the stamina band
   `0x4088–0x408F`:
   - `bool Exhausted` and `uint RefusalCount` (a running count of refusals).
   - `StaminaService.QueueStatusIfChanged` pushes it only when either value changes.
   - The stamina value itself stays in `PlayerStatsSync`, which is unchanged. This keeps ADR-029's `PlayerStatsSync`
     edits conflict-free.
   - The change is additive: an older client ignores the unknown opcode.
7. **D7 — Client.**
   - `SyncedStaminaConfig`, owned by the session, exposes the synced document. The desktop no longer contains stamina
     numbers.
   - `MultiplayerSession.Stamina.cs` predicts sprint drain and regen with `StaminaMeter`. It mirrors the server's
     exhaustion from `StaminaStatusSync`, and `PlayerStatsSync` overwrites the predicted value.
   - While exhausted, the client refuses a swing or harvest locally instead of sending it (the server would refuse it
     anyway). The bar flashes and the HUD shows `stamina.too_tired`. A skill whose cost exceeds the predicted meter is
     refused the same way, with `hud.not_enough_stamina` when the player is not exhausted.
   - The bar flashes red for 0.6 s whenever the server's `RefusalCount` grows.
   - While exhausted, the bar turns a dull ochre (`StaminaExhaustedFill`), pulses like slow breathing (one breath every
     1.6 s) and reads `stamina.exhausted` instead of the numbers.
   - New `[stamina]` section in `en.ini` and `vi.ini`. The server sends no text.
8. **D8 — Seams for other work.**
   - **Status effects (ADR-029)** feed `StaminaRegenContext.EffectScale`: the tick passes
     `StatusEffectService.GetStaminaRegenMultiplier` to `StaminaService.Regenerate`, so an effect's stamina regen
     modifier stacks with fatigue and hunger under the same `minimumRegenScale` floor.
     The derived `tired` and `starving` effects carry no stamina modifier: they display the needs state, and its regen
     penalty lives once, in `fatigueRegenScale` and `hungerRegenScale`. Timed effects such as `rested` (×1.25) stack on
     top.
   - **Swimming (ADR-032)** should add a `swimDrainPerSecond` to `stamina.json` and call `StaminaMeter.TryDrain` from the
     tick, exactly like sprint, so exhaustion and regen delay apply to swimming too.
   - A **dodge or roll** does not exist. When it lands it pays through `StaminaService.TrySpend` with its own config
     entry.
   - There is **no digging tool** yet. A shovel would be a `Tool` and use `swingCosts.tool` or an `itemSwingCosts`
     entry.

### Tuning (original; superseded by the 2026-11-27 amendment below)

A level 1 character has 100 stamina. The attack cooldown is 0.5 s at 30 Hz.

| Activity | Cost | Full pool buys |
| :--- | :--- | :--- |
| Sprint | 16/s (was 22/s) | 6.25 s of sprint |
| Wood axe swing (oak: 100 HP / 55 per hit) | 8 | 12 swings, about 6 oaks |
| Iron axe swing (pine: 140 HP / 80 per hit) | 10 | 10 swings, 5 pines |
| Stone pickaxe swing (boulder: 140 HP / 68 per hit) | 10 | 10 swings, about 3 boulders |
| Crystal pickaxe swing (gold vein: 320 HP / 95 per hit) | 12 | 8 swings, 2 veins |
| Bare-hand swing (bush: 40 HP / 20 per hit; oak: 100 HP / 8 per punch since #129) | 4 | 25 swings: 12 bushes, or about 2 oaks |
| Copper sword / iron sword / demon cleaver / ember blade | 8 / 10 / 14 / 15 | 12 / 10 / 7 / 6 swings |
| Bow shot (hunting / bone / crystal) | 6 / 7 / 8 | 16 / 14 / 12 shots |
| Staff cast | 0 (mana) | — |
| Building or demolition work | 4/s | a wood wall (2 s of work) costs 8 |

Recovery:

| Situation | Rate | Time |
| :--- | :--- | :--- |
| Standing, fed, rested | 6/s after a 1.75 s delay (was 18/s, no delay) | 0 → full in about 18 s (was 5.6 s) |
| Walking | 3/s | 0 → full in about 35 s |
| Sitting or sleeping | 9/s | 0 → full in about 13 s |
| Exhausted at 0, standing | — | about 7.6 s until 35% unlocks actions |
| Tired / very tired / exhausted rest | ×0.8 / ×0.6 / ×0.4 | — |
| Hungry / urgently hungry / starving | ×0.85 / ×0.6 / ×0.4 | — |
| Floor (any combination) | ×0.25 minimum | — |

Chopping until refused takes 12 swings, about 6 s. The 13th swing (at 4 stamina) is refused and exhausts the player.
After the 1.75 s delay plus 31 / 6 s, the player is back to 35%: about 7 s of rest for 4 more swings. Sustained
work is therefore about 4 swings every 9 s instead of 2 swings a second, while a single fight or a single tree still
feels normal. That gap is the "meaningful wait".

**No soft-lock for a fresh character.** Every hero now starts empty-handed (#132). With bare hands a bush costs 8
stamina, and punching an oak for the first logs (#129: 13 punches) costs 52, about half a level-1 pool, then about 18 s
of rest. The first stone axe is therefore a few minutes away, which is the hardcore start the owner asked for, never a
dead end:
- Regen never stops: the floor is `minimumRegenScale` 0.25, and validation rejects 0.
- Even starving and exhausted from lack of sleep, a player still regenerates 1.5/s standing, so exhaustion lifts in
  about 25 s.
- Nothing needed to recover costs stamina: walking, eating, sitting and sleeping are free.

## Alternatives Considered

- **Weakened actions at low stamina** (less damage, more wear). Rejected (D3): it needs a second damage table, the client
  cannot predict it as cleanly, and it still allows spamming at 1 stamina.
- **Exhaustion only at exactly 0.** Rejected. Refused actions would then let a player act again the moment regen
  covers one swing (a wood-axe swing about every 3 s forever). Refusal-triggered exhaustion makes the lock consistent.
- **Adding exhaustion to `PlayerStatsSync`.** Rejected to avoid colliding with ADR-029's appends on the same packet. A
  separate packet in the allocated band also only sends on change.
- **A new `OpStatus.NotEnoughStamina`.** Rejected for now. `RateLimited` already means "retry later", the HUD learns
  the reason from `StaminaStatusSync`, and appending to a shared wire enum in parallel with other sessions risks value
  clashes.
- **Charging cost only on a hit.** Rejected: whiffing is physical work too, and charging up front keeps the check before
  any world mutation.

## Consequences

- Gathering, combat and building now pace each other. Better tools pay off twice: more damage per swing and fewer swings
  per resource.
- Sprint is cheaper per second, but recovery is three times slower and waits 1.75 s, so running everywhere is no longer
  free.
- The desktop's last hardcoded stamina numbers are gone. Tuning is one hot-reloadable file.
- Exhaustion and the regen delay are not persisted. A relog clears them, but logging in again takes longer than the
  exhaustion wait, so this is accepted rather than adding a migration.
- `IWorldOutboundSink.SendStaminaStatus` and `ApplyGameData(..., stamina)` are additive with defaults. Existing sinks and
  callers compile unchanged.

## Amendment: playable tuning (2026-11-27)

### Context

The owner played the hardcore tuning and reported: "Stamina is too low. A few swings and it's empty. How is anyone
supposed to play like that?" Stamina must stay meaningful, but be playable. A level-1 player must get through a normal
fight against a same-level monster without hitting zero, and chop a tree stopping at most once.

### Measurement

`tests/server/Carbon.Host.Tests/StaminaPacingHarness.cs` plays each activity in a real `AuthoritativeWorld` on the shipped
config. It sends the same inputs a client sends (move, attack, harvest key) and reads only what the server sends back.
Typical builds:

- **Level 1**: no attribute points, wood sword, wood axe.
- **Level 10**: 41 points split evenly (8 power, 8 agility), copper sword, stone axe.
- **Level 30**: 141 points split evenly (28 power, 28 agility), iron sword, iron axe.

The level-1 and level-10 fights are against a wild slime of the same level. Slimes stop at level 15, so the level-30 fight
is against the first non-elite level-30 monster the scouts meet (a skeleton archer). "Swings to refusal" is continuous
swinging from a full bar. "60 s of swinging" holds the attack button for a minute and counts paid swings and stops; the
client does not send swings while exhausted.

| Level | Max | Swings to refusal | Oak: s / swings / stops | Slime fight: s / hits / lowest stamina / exhausted | Sprint s | Rest: unlock / full s | 60 s of swinging: swings / stops |
| :--- | ---: | ---: | :--- | :--- | ---: | :--- | :--- |
| 1 before | 100 | 16 | 1.0 / 3 / 0 | 1.7 / 5 / 70 / 0 | 6.3 | 6.5 / 17.4 | 46 / 7 |
| 1 after | 100 | **25** | 1.0 / 3 / 0 | 1.7 / 5 / 80 / 0 | **8.3** | **2.8 / 8.2** | **87 / 11** |
| 10 before | 209 | 26 | 0.5 / 2 / 0 | 2.6 / 6 / 161 / 0 | 13.1 | 13.3 / 35.9 | 44 / 3 |
| 10 after | 209 | **41** | 0.5 / 2 / 0 | 2.6 / 6 / 179 / 0 | **17.4** | **4.0 / 15.2** | **81 / 5** |
| 30 before | 469 | 46 | 0.5 / 2 / 0 | 3.3 / 8 / 389 / 0 | 29.3 | 27.2 / 78.0 | 62 / 2 |
| 30 after | 469 | **78** | 0.5 / 2 / 0 | 3.3 / 8 / 421 / 0 | **39.1** | **8.9 / 34.0** | **97 / 2** |

What the numbers show:

- A single slime or a single oak was never the problem. The slime dies in 5 hits and the oak in 3 swings, at either
  tuning.
- The problem was **recovery**. The bar refilled at 6/s, only after 1.75 s without a spend, and exhaustion held until 35%.
  A level-1 hero who emptied the bar waited 6.5 s to act again and 17.4 s for a full bar. Holding attack for a minute
  gave 46 swings, a third of the 150 the cooldown allows. Real fights add misses, a slime pack (`pack: slime`) and
  a run between targets, so "a few swings and it's empty" is what the owner felt.

Reference games (approximate, from public wikis):

- **Minecraft**: no stamina bar. Sprint and attacks drain hunger very slowly, so a fight is effectively free and sprint
  lasts minutes.
- **Dark Souls**: a small bar (5–8 light swings) that comes back very fast: regen starts within about a second and a full
  bar returns in 2–3 s. Each swing matters; waiting is short.
- **Valheim**: a 50–200 bar by food. Regen starts about 1 s after the last use and a bar refills in a few seconds while
  standing. Tree felling needs several short pauses at the start of the game.

The new tuning sits between Dark Souls and Valheim: a bar that lasts a whole level-1 fight (25 swings), a 1 s delay, and
a breather of about 3 s instead of 6.5 s.

### Decision

`stamina.json` changes. The pool (`progression.json`) does not: the measurements show the pool is fine, and recovery is
what hurt.

| Field | Before | After |
| :--- | ---: | ---: |
| `sprintDrainPerSecond` | 16 | 12 |
| `regenDelaySeconds` | 1.75 | 1 |
| `regenPerSecondStill` | 6 | 14 |
| `regenPerSecondMoving` | 3 | 8 |
| `exhaustedRecoverFraction` | 0.35 | 0.25 |
| `swingCosts` bare hand / melee / tool / ranged | 4 / 9 / 9 / 6 | 2 / 5 / 5 / 4 |
| wood axe / wood pickaxe / wood sword | 8 / 8 / 6 | 4 / 4 / 4 |
| stone axe / iron axe | 9 / 10 | 5 / 6 |
| stone pickaxe / crystal pickaxe | 10 / 12 | 6 / 7 |
| copper sword / iron sword / demon cleaver / ember blade | 8 / 10 / 14 / 15 | 5 / 6 / 8 / 9 |
| hunting bow / bone bow / crystal bow | 6 / 7 / 8 | 4 / 4 / 5 |

Unchanged: `swimDrainPerSecond` 5, `buildWorkPerSecond` 4, `restingRegenMultiplier` 1.5, the fatigue and hunger scales,
and `minimumRegenScale` 0.25.

Kept from the original decision: a swing that costs more than the meter holds is refused (D3); the exhausted state and
its recover fraction (D4); wood tools cost less than the next tier; death and respawn (D5); swimming drain (D8).

Recovery after the amendment:

| Situation | Rate | Time |
| :--- | :--- | :--- |
| Standing, fed, rested | 14/s after 1 s | 0 → full in about 8 s (was 18 s) |
| Walking | 8/s | 0 → full in about 13.5 s (was 35 s) |
| Sitting or sleeping | 21/s | 0 → full in about 6 s |
| Exhausted at 0, standing | — | about 2.8 s until 25% unlocks actions (was 6.5 s) |

### Tests

`tests/server/Carbon.Host.Tests/StaminaPacingTests.cs` runs on the shipped config and pins outcomes, not the formula:

- A level-1 hero with a wood sword kills a level-1 slime without being exhausted.
- A level-1 hero fells an oak with the wood axe stopping at most once.
- A full level-1 bar gives at least 20 swings.
- After exhaustion, a level-1 hero acts again within 4 s and has a full bar within 10 s.
- A swing that costs more than the meter holds is refused, leaves the oak untouched and is reported.
- After a swing, regen waits for the configured delay and then starts.

Mutation results (each change was made alone, then reverted):

| Mutation | Tests that failed |
| :--- | :--- |
| `stamina.json` back to the hardcore tuning | full bar gives 20 swings; acts again after a breather |
| `wood_sword` 4 → 20 | kills a level-1 slime; full bar gives 20 swings |
| `wood_axe` 4 → 60 | fells an oak stopping at most once |
| regen still / moving 14 / 8 → 6 / 3 | acts again after a breather |
| `regenDelaySeconds` 1 → 4 | acts again after a breather |
| `exhaustedRecoverFraction` 0.25 → 0.6 | acts again after a breather |
| `StaminaMeter.TrySpend` never refuses | unaffordable swing is refused |
| a spend does not start the regen delay | regen waits for the delay; acts again after a breather; unaffordable swing is refused |
| a spend sets 3× the configured delay | regen waits for the delay; acts again after a breather |
| a refusal does not exhaust | none of these; `StaminaMeterTests.TrySpend_MoreThanTheMeterHolds_RefusesWithoutDebitingAndExhausts` fails |

### Consequences

- Stamina still paces play: emptying the bar stops the hero for about 3 s, and holding attack for a minute is about 87
  swings instead of the 150 the cooldown allows.
- Better tools still pay twice: they cost more per swing but need fewer swings.
- The level-1 start stays slow because bare hands are weak (#129), not because of stamina.

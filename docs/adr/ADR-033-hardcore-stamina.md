# ADR-033: Hardcore Stamina — Every Physical Action Costs

- **Date**: 2026-11-27
- **Status**: Accepted
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
     fatigue × hunger × effect scale, floored at `minimumRegenScale`.
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
   - **Respawn** (death and starvation) refills the meter and clears exhaustion.
6. **D6 — Wire.** `StaminaStatusSync` (S2C, `ItemOpCode.StaminaStatusSync = 0x4088`), allocated from the stamina band
   `0x4088–0x408F`:
   - `bool Exhausted` and `uint RefusalCount` (a running count of refusals).
   - `StaminaService.QueueStatusIfChanged` pushes it only when either value changes.
   - The stamina value itself stays in `PlayerStatsSync`, which is unchanged. This keeps ADR-029's `PlayerStatsSync`
     edits conflict-free.
   - The change is additive: an older client ignores the unknown opcode.
7. **D7 — Client.**
   - `SyncedStaminaConfig` exposes the synced document. The desktop no longer contains stamina numbers.
   - `MultiplayerSession.Stamina.cs` predicts sprint drain and regen with `StaminaMeter`. It mirrors the server's
     exhaustion from `StaminaStatusSync`, and `PlayerStatsSync` overwrites the predicted value.
   - While exhausted, the client refuses a swing or harvest locally instead of sending it (the server would refuse it
     anyway). The bar flashes and the HUD shows `stamina.too_tired`. A skill whose cost exceeds the predicted meter is
     refused the same way.
   - The bar flashes red for 0.6 s whenever the server's `RefusalCount` grows.
   - While exhausted, the bar turns a dull ochre (`StaminaExhaustedFill`), pulses like slow breathing (one breath every
     1.6 s) and reads `stamina.exhausted` instead of the numbers.
   - New `[stamina]` section in `en.ini` and `vi.ini`. The server sends no text.
8. **D8 — Seams for other work.**
   - **Status effects (ADR-029)** have not landed. `StaminaRegenContext.EffectScale` (1 today) is where their stamina
     regen modifiers multiply in. Once it is fed, `StaminaService.Regenerate` is the only caller to change.
   - **Swimming (ADR-032)** should add a `swimDrainPerSecond` to `stamina.json` and call `StaminaMeter.TryDrain` from the
     tick, exactly like sprint, so exhaustion and regen delay apply to swimming too.
   - A **dodge or roll** does not exist. When it lands it pays through `StaminaService.TrySpend` with its own config
     entry.
   - There is **no digging tool** yet. A shovel would be a `Tool` and use `swingCosts.tool` or an `itemSwingCosts`
     entry.

### Tuning (shipped `stamina.json`)

A level 1 character has 100 stamina. The attack cooldown is 0.5 s at 30 Hz.

| Activity | Cost | Full pool buys |
| :--- | :--- | :--- |
| Sprint | 16/s (was 22/s) | 6.25 s of sprint |
| Wood axe swing (oak: 100 HP / 55 per hit) | 8 | 12 swings, about 6 oaks |
| Iron axe swing (pine: 140 HP / 80 per hit) | 10 | 10 swings, 5 pines |
| Stone pickaxe swing (boulder: 140 HP / 68 per hit) | 10 | 10 swings, about 3 boulders |
| Crystal pickaxe swing (gold vein: 320 HP / 95 per hit) | 12 | 8 swings, 2 veins |
| Bare-hand swing (bush: 40 HP / 20 per hit) | 4 | 25 swings, 12 bushes |
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

**No soft-lock for a fresh character.** With an empty inventory (bare hands), a bush costs 8 stamina.
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

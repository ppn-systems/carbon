# Survival Needs (Hunger, Rest, Mood)

`engine/Carbon.Survival` adapts RimWorld's needs model to a real-time MMORPG player. All tuning lives in
[`resources/config/needs.json`](../resources/config/needs.json) and
[`resources/config/food.json`](../resources/config/food.json); the numbers below are the shipped defaults.
Rates are per **real minute** and independent of the tick rate.

## Food / hunger

Food is 0..100. It drains at `1.6667 × activity multiplier` per minute (walking = ×1).

| Activity | Multiplier | Full → Hungry (25 %) | Full → Urgently hungry (12.5 %) | Full → Starving (0) |
| :--- | ---: | ---: | ---: | ---: |
| Resting | 0.4 | 112.5 min | 131.3 min | 150 min |
| Idle | 0.5 | 90 min | 105 min | 120 min |
| Walking | 1.0 | 45 min | 52.5 min | **60 min** |
| Fighting | 1.75 | 25.7 min | 30 min | 34.3 min |
| Sprinting | 2.0 | 22.5 min | 26.3 min | 30 min |
| Swimming | 2.5 | 18 min | 21 min | 24 min |

| State | Threshold | Mood | Move / attack speed | Health regen |
| :--- | :--- | ---: | ---: | :--- |
| Fed | ≥ 25 % | 0 | ×1.0 | yes |
| Hungry | < 25 % | −6 | ×1.0 | yes |
| UrgentlyHungry | < 12.5 % | −12 | ×0.8 | **blocked** |
| Starving | 0 | −20 | ×0.65 | **blocked** |

### Starvation and malnutrition

- While starving, malnutrition severity rises **0.1 / min**: starving → death in **10 minutes** (severity 1.0 is
  lethal no matter how much health remains).
- Every **5 s** a starvation pulse deals `0.25 + 1.0 × severity` damage (0.25 at onset, 1.25 near the end).
  Over the full 10 minutes that is 90.5 damage, so a 100 HP character dies from malnutrition at 10:00 and a
  50 HP character dies from the pulses after about 7 minutes.
- Pulses are published as `DamageEvent` / `EntityKilledEvent` with the player as their own attacker.
- Once food is above 0, malnutrition recovers at **0.05 / min** (severity 0.5 clears in 10 minutes) and
  pulses stop.

## Eating

`EatActionSystem.BeginEat(world, player, itemId)` is server-authoritative and returns an `OpStatus`:

| Result | When |
| :--- | :--- |
| `Ok` | Meal started |
| `Invalid` | Empty item id, or the entity has no `NeedsComponent` |
| `NotFound` | Entity handle is dead, or the item is not in `food.json` |
| `Forbidden` | Eater is dead |
| `Conflict` | Already eating, or food ≥ 90 % (overeating cap) |

The meal takes `eatSeconds`. Any health drop (except starvation pulses) interrupts it with
`MealInterruptedEvent(Damaged)`; `CancelEat` interrupts it with `Cancelled`. Interrupted meals give nothing
and consume nothing. On completion nutrition is added (capped at 100, excess wasted), food poisoning is
rolled, the food's mood memory is added, and `MealCompletedEvent` is published. **The server removes one
item from the inventory when it handles `MealCompletedEvent`.**

| Food (`items.json` id) | Quality | Nutrition | Eat time | Poison chance | Mood memory |
| :--- | :--- | ---: | ---: | ---: | :--- |
| `berries` | raw | 10 | 1.5 s | 2 % | — |
| `slime_jelly` | raw | 15 | 2 s | 8 % | −3 for 5 min |
| `raw_meat` | raw | 25 | 4 s | 15 % | −7 for 10 min |
| `bread` | simple | 35 | 3 s | 1 % | — |
| `cooked_meat` | simple | 40 | 4 s | 2 % | +2 for 10 min |
| `roast_meat` | simple | 45 | 4 s | 0 % | +3 for 10 min |
| `arcane_pie` | fine | 50 | 5 s | 0 % | +5 for 15 min |
| `stew` | fine | 60 | 6 s | 1 % | +5 for 15 min |

`roast_meat` and `arcane_pie` are the edible consumables in today's `StarterGameCatalog`; their HP/MP/stamina
restores are still applied, but at the end of the meal instead of instantly. A consumable whose code is not in
`food.json` keeps the old instant potion behaviour.

Food poisoning lasts **2 minutes**: mood −12, move/attack speed ×0.85, health regen blocked. The roll is
`FoodPoisoningRoll.Sample(seed, eater, tickIndex, foodIndex)`, a pure SplitMix64 hash, so the same world seed
replays identically and no RNG state is stored.

## Food spoilage (ADR-016)

Raw food spoils while its owner is online. Stacks are checked about once a second.

| Food | Spoils after | Becomes |
| :--- | ---: | :--- |
| `raw_meat` | 20 min | `rotten_food` |
| `slime_jelly` | 30 min | `rotten_food` |
| `berries` | 40 min | `rotten_food` |
| cooked food, `bread`, `stew` | never | — |

- `rotten_food` is still edible: +5 food, 60 % food poisoning, −10 mood for 10 min.
- New stacks start their countdown on the first spoilage pass. Merging stacks keeps the older countdown.
- The hotbar tooltip shows `+25 food  Spoils in 12m`.
- Food on the ground does not age; it expires instead.
- `spoilSeconds` and `spoilsInto` live in `food.json`.

## Rest

**Rest is disabled in the shipped `needs.json` (`restEnabled: false`)** until the game has a sit/sleep action;
with it off, rest stays full and has no effect. The engine default is on. When enabled: rest is 0..100 and drains `0.8333 × multiplier` per minute while awake (idle ×0.8, walking ×1, sprint / fight /
swim ×1.5). The `Resting` activity recovers **10 / min** (empty → full in 10 minutes).

| State | Threshold | Walking time from full | Mood | Speed |
| :--- | :--- | ---: | ---: | ---: |
| Rested | ≥ 28 % | — | 0 | ×1.0 |
| Tired | < 28 % | 86.4 min | −6 | ×1.0 |
| VeryTired | < 14 % | 103.2 min | −12 | ×1.0 |
| Exhausted | 0 | 120 min | −18 | ×0.85 |

## Mood

Mood is 0..100: baseline 50 plus the hunger, rest and poisoning thoughts plus up to 4 timed memories (one per
kind; eating the same quality again refreshes it). Bands: Miserable < 20, Stressed < 35, Neutral,
Content ≥ 65, Happy ≥ 85. A starving player is at 30 (Stressed); starving and exhausted is 12 (Miserable).

Speed penalties multiply: starving + exhausted + poisoned = 0.65 × 0.85 × 0.85 ≈ ×0.47.

## Server integration (`server/Carbon.Application/World/AuthoritativeWorld.Survival.cs`)

- The host builds `NeedsConfig` / `FoodCatalog` from the `ConfigRepository` snapshot (ADR-014), so the
  survival data is the same bytes the server serves to clients; bad data stops the server at boot.
- `EatActionSystem` then `NeedsSystem` are registered on the world simulation; every player entity gets
  `NeedsComponent.CreateFull`.
- Each tick the player's activity comes from server state: Fighting (hit in the last 6 s) > Sprinting >
  Walking (moving) > Idle. Swimming is not detected yet.
- Eating: using an edible consumable from the hotbar (the existing primary-action / `AttackInput` handler)
  calls `BeginEat`. The item stays reserved in its slot; moving or dropping it cancels the meal. On
  `MealCompletedEvent` one item is consumed and the slot change is sent. Monster damage interrupts the meal.
- Effects: walk/sprint speed × `MoveSpeedScale`, basic attack cooldown ÷ `AttackSpeedScale`, out-of-combat
  HP regen is skipped while `HealthRegenBlocked`.
- Starvation death goes through the normal death/respawn path (full HP at the sanctuary). Any death clears
  malnutrition and poisoning and leaves food at no less than 50 %.
- Sync: `PlayerStatsSync` gained `Food`, `MaxFood`, `HungerState`, `Malnutrition`, `Mood`, `FoodPoisoned`
  and `EatProgress` (serialize orders 18–24, appended). It is sent every 10 ticks and right after any
  threshold change, meal start or meal end.
- Persistence (ADR-015): food, malnutrition and remaining poison are saved on the character (migration
  `0006_character_needs`) and restored on login, clamped to the current config. Needs pause while logged
  out. Characters saved before the migration spawn fully fed. Mood memories and in-progress meals are not
  saved.

## Client

`IWorldReadModel` exposes `LocalPlayerFood`, `LocalPlayerMaxFood`, `LocalPlayerHungerState`,
`LocalPlayerMalnutrition`, `LocalPlayerMood`, `LocalPlayerFoodPoisoned` and `LocalPlayerEatProgress`. The vitals
cluster above the hotbar (`HotbarAndItemWidget`) has a fourth bar for hunger. Its colour follows the state
(amber fed, orange hungry, red starving, green poisoned), its label reads "Food 82%", "Hungry 20%",
"Ravenous 10%" or "Starving!", and a white strip shows meal progress while eating.

## Engine wiring (other hosts)

1. Load `NeedsConfig.LoadFromJson` and `FoodCatalog.LoadFromJson(json, config.FoodMax)`.
2. Register `EatActionSystem` then `NeedsSystem` (both `PostUpdate`).
3. Add `NeedsComponent.CreateFull(config)` and set `Activity` every tick (`NeedsSystem.SetActivity`).
4. Read `MoveSpeedScale`, `AttackSpeedScale` and `HealthRegenBlocked`; consume the item on `MealCompletedEvent`.
5. HUD: `NeedStateChangedEvent(entity, need, previous, current, level)`. If the host never calls
   `WorldSimulation.Update`, clear the survival event buffers (and the starvation `DamageEvent` /
   `EntityKilledEvent`) after handling them, as the server does.

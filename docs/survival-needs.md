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
| `stew` | fine | 60 | 6 s | 1 % | +5 for 15 min |

Food poisoning lasts **2 minutes**: mood −12, move/attack speed ×0.85, health regen blocked. The roll is
`FoodPoisoningRoll.Sample(seed, eater, tickIndex, foodIndex)`, a pure SplitMix64 hash, so the same world seed
replays identically and no RNG state is stored.

## Rest

Rest is 0..100 and drains `0.8333 × multiplier` per minute while awake (idle ×0.8, walking ×1, sprint / fight /
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

## Integration

1. Load `NeedsConfig.LoadFromJson` and `FoodCatalog.LoadFromJson(json, config.FoodMax)` at startup; both
   validate every value and throw `InvalidDataException` on bad data.
2. Register `EatActionSystem` then `NeedsSystem` (both `PostUpdate`) so a finished meal updates the hunger
   state on the same tick.
3. Add `NeedsComponent.CreateFull(config)` to player entities and set `Activity` every tick
   (`NeedsSystem.SetActivity`).
4. Movement, combat and regen code read `MoveSpeedScale`, `AttackSpeedScale` and `HealthRegenBlocked`.
5. HUD: `NeedStateChangedEvent(entity, need, previous, current, level)` on every threshold crossing; `Need`
   selects whether the state bytes are `HungerState`, `RestState` or `MoodState`.

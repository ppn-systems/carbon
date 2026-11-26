# Carbon Economy Model

How gold and diamonds enter and leave the world, how loot is rolled, and how crafting turns materials into value.
Every number below is in `resources/config/*.json`. The tests in `tests/engine/Carbon.Economy.Tests` check the configs against these numbers, so a balance regression fails CI.

| File | Owns |
| :--- | :--- |
| `resources/config/items.json` | Item ids, codes, rarity, tier, stack size, **base price**, durability, vendor flag, theme tags (nutrition lives in the survival agent's `food.json`) |
| `resources/config/loot.json` | Loot table per monster family / gather node |
| `resources/config/recipes.json` | Recipes: station, work ticks, skill/level gates, ingredients, outputs |
| `resources/config/economy.json` | Currency caps, gold/diamond curves, pity, vendor spread, repair, crafting fees, design targets |

Code: `engine/Carbon.Economy` (wallet, loot, vendor, fees) and `engine/Carbon.Items` (item catalog, recipes, atomic crafting, crafting jobs).

## 1. Design principles

Proven survival/MMO economies (RuneScape, WoW, EVE, Diablo, Terraria, RimWorld) share a few rules:

1. **Faucets vs sinks.** Every source of currency (faucet) needs matching destruction (sink), or prices inflate and new players can't keep up. Our faucets are monster gold, vendor sales and diamonds. Our sinks are vendor purchases, the vendor buy/sell spread, repairs and station fees.
2. **Sub-linear income growth.** Gold per kill grows as `level^0.6`. Doubling the level gives about 1.5× the gold, not 2× (EVE/RuneScape-style damping). Gear prices also rise by tier, so repair and fee sinks grow with income.
3. **No arbitrage.** Vendors pay 25% of base price and charge 100%. Buying ingredients, crafting and selling always loses gold, and a test checks this for every recipe.
4. **Gray monsters pay little.** A monster more than 8 levels below the player is *trivial* (the WoW "gray mob" rule). It pays 20% gold, can't drop diamonds and doesn't advance pity, so farming starter zones is pointless.
5. **Premium currency stays rare.** Diamonds come mainly from elites (×5), bosses (×40), deep/distant zones (+25% per distance tier) and rich ore veins. Bad-luck protection puts a floor on income without making diamonds common.
6. **Server-authoritative and deterministic.** The server seeds a `DeterministicRandom` per kill (`LootRoller.CreateRandom(worldSeed, tick, sourceId)`), rolls and sends the result. Clients never roll. The same inputs always give the same loot, so audits can be replayed.
7. **Atomic mutations.** Each wallet, vendor or crafting operation either fully succeeds or changes nothing. It returns `OpStatus` (wire) plus a detailed reason (`EconomyError` / `CraftFailure`).

## 2. Currencies (`Carbon.Economy.Currency`)

| Currency | Role | Cap (`economy.json → currencies`) |
| :--- | :--- | :--- |
| Gold | Soft currency: monster drops, vendor trade, repairs, fees | 2,000,000,000 |
| Diamond | Hard/premium currency: elite/boss/deep monsters, rich ore | 1,000,000 |

Currencies live in a `Wallet`, not in inventory slots, so they can't be dropped, duplicated by inventory bugs or stacked past a cap.

- Balances are `long`, never negative and never above the cap. Credits beyond the cap are rejected in full, with no partial credit and no wrap-around (`TryCredit(long.MaxValue)` followed by `+1` is refused).
- `TrySpend` checks and debits in one call. `TrySpend(gold, diamonds)` debits both or neither. `Wallet.TryTransfer` checks the payer's funds *and* the payee's cap before moving anything.
- Every committed change bumps a gap-free per-wallet `Sequence` and emits a `CurrencyLedgerEntry` (owner, sequence, currency, signed delta, balance after, `CurrencyReason`, tick, counterparty) to the `ICurrencyLedger`. `CurrencyLedgerBuffer` keeps recent entries for draining to persistence and tracks **minted vs burned** per currency for faucet/sink telemetry. Trades are neutral.
- Wallets are mutated only on the world tick thread (see the `carbon-handlers` skill), so a check followed by a commit within one handler can't race.

| `EconomyError` | `OpStatus` |
| :--- | :--- |
| `InvalidAmount`, `SameWallet`, `CraftRejected` | `Invalid` |
| `InsufficientFunds`, `BalanceCapExceeded`, `InsufficientItems`, `AlreadyRepaired` | `Conflict` |
| `UnknownItem` | `NotFound` |
| `NotForSale`, `NotRepairable` | `Forbidden` |
| `InventoryFull` | `InventoryFull` |

## 3. Loot (`Carbon.Economy.Loot`)

Each table in `loot.json` is keyed by source id. Every `lootTableId` in `monsters.json` has a table, and a test checks this, including the elite flag. The families are `slime`, `skeleton`, `orc`, `blood_monster`, `demon`, `golem`, `wolf` and `wraith`. The elite variants `slime_king` and `orc_warlord` are marked `"elite": true`, so they roll with elite multipliers and are excluded from the normal-monster income bands. There is also one gather node, `rich_ore_vein`.

A roll proceeds in this order:

1. **Guaranteed entries.** These always drop when the monster level is inside the entry's `[minLevel, maxLevel]` band (e.g. every slime drops 1–2 `slime_gel`).
2. **Weighted rolls.** There are `rolls` draws, plus `eliteExtraRolls` (default 1) for elites or `bossExtraRolls` (default 3) for bosses. Each draw picks from the level-eligible entries plus `nothingWeight`. Level bands unlock items with depth, e.g. `slime_core` from slime level 5 and `rough_gem` from level 10.
3. **Gold.** See §4.
4. **Diamonds.** See §5.

The roll path is zero-allocation. The caller passes a `Span<LootDrop>` sized with `LootTable.MaxDrops(context)`. Duplicate items are merged, and overflow sets `Truncated` instead of writing past the buffer. The level curves are precomputed into integer lookup tables at load time, so rolling uses integer math only. A test checks for zero allocated bytes over 10,000 rolls.

**Themes.** Each table may drop only items tagged with its theme (its id, or `theme` for gather nodes) or `generic`, and a test enforces this. Slimes can never drop orc tusks.

| Table | Designed levels | Guaranteed | Weighted highlights (unlock level) |
| :--- | :--- | :--- | :--- |
| `slime` | 1–15 | `slime_gel` 1–2 | `slime_gel`, `slime_jelly`, `plant_fiber`, `small_health_potion` (3), `slime_core` (5), `rough_gem` (10) |
| `skeleton` | 8–35 | `bone` 1–3 | `bone`, `bone_dust`, `bone_shard`, `rope`, `small_health_potion` (10), `rough_gem` (20) |
| `orc` | 15–45 | `crude_iron` 1–2 | `crude_iron`, `raw_meat`, `beast_pelt`, `orc_tusk` (18), `small_health_potion`, `rough_gem` (25) |
| `blood_monster` | 30–70 | `blood_vial` 1 | `blood_vial`, `raw_meat`, `small_health_potion`, `rough_gem` (40) |
| `demon` | 45–100 | `ember_shard` 1–2 | `ember_shard`, `coal`, `demon_ember` (55), `demon_horn` (60), `small_health_potion`, `rough_gem` (50) |
| `slime_king` (elite) | 5–20 | `slime_gel` 3–6, `slime_core` 1 | `slime_jelly`, `slime_core`, `small_health_potion`, `rough_gem` (10) |
| `orc_warlord` (elite) | 20–50 | `crude_iron` 2–4, `orc_tusk` 1–2 | `orc_tusk`, `crude_iron`, `beast_pelt`, `small_health_potion`, `rough_gem` (25) |
| `golem` | 10–40 | `stone_chunk` 2–4 | `stone_chunk`, `iron_ore`, `stone_core` (15), `rough_gem` (20) |
| `wolf` | 5–30 | `raw_meat` 1–2 | `wolf_pelt`, `wolf_fang` (8), `raw_meat`, `beast_pelt` |
| `wraith` | 20–60 | `ectoplasm` 1–2 | `ectoplasm`, `mana_crystal`, `ash_essence` (30), `small_health_potion`, `rough_gem` (35) |
| `rich_ore_vein` | 1–100 | `iron_ore` 2–4 | `copper_ore`, `coal`, `iron_ore`, `mana_crystal` (10), `rough_gem` (20) |

## 4. Gold faucet

```
mean(level)  = round(goldBase × level^0.6)                  (≥ 1 when goldBase > 0)
multiplier   = ×3 elite | ×12 boss,  × (100 + 10 × distanceTier[0..10]) %,  × 20 % if trivial
gold         = mean × multiplier × uniform(75 %..125 %)     (normal monsters drop gold 70 % of the time; elites/bosses always)
```

Expected income for an engaged player at **120 kills/hour** against level-appropriate normal monsters. The test runs 10,000 kills per row and checks these figures:

| Table | Level | Mean/kill | Expected gold/kill | **Gold/hour** | Design band |
| :--- | ---: | ---: | ---: | ---: | :--- |
| `slime` (base 1) | 1 | 1 | 0.7 | **84** | 50–900 |
| `slime` | 15 | 5 | 3.5 | **420** | 250–2,000 |
| `skeleton` (base 2) | 8 | 7 | 4.9 | **588** | 50–900 |
| `skeleton` | 35 | 17 | 11.9 | **1,428** | 800–3,500 |
| `orc` (base 2) | 30 | 15 | 10.5 | **1,260** | 800–3,500 |
| `orc` | 45 | 20 | 14.0 | **1,680** | 800–3,500 |
| `blood_monster` (base 3) | 50 | 31 | 21.7 | **2,604** | 1,500–5,000 |
| `blood_monster` | 70 | 38 | 26.6 | **3,192** | 1,500–5,000 |
| `demon` (base 3) | 72 | 39 | 27.3 | **3,276** | 1,500–5,000 |
| `demon` | 100 | 48 | 33.6 | **4,032** | 2,000–6,000 |

From level 1 to level 100, income rises about 50×. Item prices rise about 30–100× from tier 1 (wood axe 15 g, leather cap 35 g) to tier 4–5 (ember blade 480 g, abyssal crown 2,500 g). At any stage, a tier-appropriate upgrade costs roughly 15–60 minutes of gold. Endgame items are gated by rare drops, not by gold alone.

## 5. Diamond faucet

```
ppm(level)  = diamondBasePpm × (100 + 10 × level) / 100
chance      = ppm × 5 elite | × 40 boss,  × (100 + 25 × distanceTier) %,  capped at 50,000 ppm (5 %)
trivial     = 0 (and pity does not advance)
```

**Bad-luck protection** (`economy.json → diamond.pity`, the Diablo/gacha "soft pity" model):

- After 10,000 eligible kills without a diamond, the chance rises by +1 ppm per further kill.
- At 30,000 kills, a diamond is guaranteed.
- The counter (`LootPityState`) is per character and resets on any diamond.

The pity floor gives every player roughly 1 diamond per ~90 hours of level-appropriate play. Higher levels, elites, bosses and depth raise that.

| Table | Level | Base ppm (normal) | **Diamonds/hour incl. pity** | Elite ppm | Boss ppm |
| :--- | ---: | ---: | ---: | ---: | ---: |
| `slime` (5) | 1 | 5 | **0.011** (1 per ~91 h) | 25 | 200 |
| `skeleton` (8) | 35 | 36 | **0.013** (1 per ~77 h) | 180 | 1,440 |
| `orc` (10) | 45 | 55 | **0.014** (1 per ~70 h) | 275 | 2,200 |
| `blood_monster` (15) | 70 | 120 | **0.020** (1 per ~51 h) | 600 | 4,800 |
| `demon` (25) | 100 | 275 | **0.035** (1 per ~29 h) | 1,375 | 11,000 (1.1 %) |
| `rich_ore_vein` (150) | 50 | 900 | per node mined; veins are rare world spawns | — | — |

Design bands for diamonds per hour from normal monsters: L1–24 0.006–0.02, L25–49 0.008–0.03, L50–74 0.01–0.04, L75–100 0.015–0.06. The test simulates 1,000,000 kills per (table, level) with pity active. It also checks that no dry streak reaches the hard cap.

## 6. Crafting (`Carbon.Items.Crafting`)

This follows the RimWorld model: a recipe needs a **station**, a **work amount** and a **skill**.

- **Stations:** `none` (hand), `workbench`, `forge`, `stove`, `alchemy`. `RecipeMatcher` returns `WrongStation` when the crafter isn't at the right one.
- **Gates:** `unlockLevel` (character level) and `skill` + `skillLevel` (`carpentry`, `smithing`, `cooking`, `alchemy`, `tailoring`).
- **Work:** `workTicks` is measured at baseline speed (30 ticks = 1 s at the default 30 Hz). A crafter contributes `100 + 4 × skill` work units per tick, capped at skill 50 (3× speed). `CraftingJobComponent` + `CraftingJobSystem` advance the work each tick and publish `CraftingJobCompletedEvent` when it's done.
- **Atomicity:** every mutation is staged on an `InventoryDraft` (a stack-allocated copy of the slots) and committed only if every ingredient was removed *and* every output, including byproducts, fit. Inventory-full and missing-ingredient failures leave the inventory untouched.
  - Timed jobs consume ingredients at start, and only if the outputs would fit at that moment.
  - On completion, `TryDeliverOutputs` adds everything or nothing. If the bag filled up in the meantime, the job stays `Completed` and delivery can be retried, so nothing is lost.
- **Multi-output:** `outputs[0]` is the primary product and later entries are byproducts. Examples: `smelt_crude_iron` gives `iron_ingot` + `iron_nails`, and `process_bones` gives `bone_dust` + `bone_shard`.
- **Value rule:** at base prices, crafting never destroys value (output value ≥ ingredient value). A test enforces this for all recipes.

`CraftFailure` → `OpStatus`:

| `CraftFailure` | `OpStatus` |
| :--- | :--- |
| `MissingIngredients`, `InsufficientFunds` | `Conflict` |
| `InventoryFull` | `InventoryFull` |
| `WrongStation` | `OutOfRange` |
| `LevelTooLow`, `SkillTooLow` | `Forbidden` |
| `InvalidRecipe` | `Invalid` |

## 7. Gold sinks (`Carbon.Economy.Vendor`, `Carbon.Economy.Crafting`)

| Sink | Rule (`economy.json`) | Example |
| :--- | :--- | :--- |
| Vendor spread | Buy at 100% of base price; sell at `floor(25%)`. Only `vendorBuyable` basics (logs, stone, fiber, coal, wheat, berries, raw meat, cloth, tier-1 tools) are sold. | `orc_tusk` (15 g) sells for 3 g |
| Repair | `ceil(basePrice × 30% × missing/max durability)`, minimum 1 g | Iron sword (110 g) at half durability costs 17 g |
| Station fee | Flat (hand 0, workbench 0, forge 2, stove 1, alchemy 5) + 2% of the outputs' base value | Abyssal crown: 5 + 50 = 55 g |

`VendorService.TryBuy` checks funds, wallet cap and inventory space before committing both sides. A full bag is never charged, and a capped wallet never loses the items it tried to sell. `CraftingCheckout` charges the station fee only when the craft (or job start) succeeds, and refuses the craft with `InsufficientFunds` before touching the inventory.

## 8. Items (`items.json`)

Ids 1–39 keep the `StarterGameCatalog` ids and codes, and a test checks category, stack size and durability against it. New content starts at id 100. Rarity runs `common` → `uncommon` → `rare` → `epic` → `legendary`.

| Group | Item codes |
| :--- | :--- |
| Materials | `wood_log`, `wood_plank`, `stone_chunk`, `stone_brick`, `plant_fiber`, `rope`, `cloth`, `leather`, `beast_pelt`, `copper_ore`, `copper_ingot`, `iron_ore`, `iron_ingot`, `iron_nails`, `coal`, `mana_crystal`, `rough_gem`, `wheat` |
| Food (`food` tag; nutrition in `food.json`) | `berries`, `raw_meat`, `cooked_meat`, `bread`, `stew`, `slime_jelly`, `roast_meat`, `arcane_pie` |
| Slime | `slime_gel`, `slime_core` (rare) |
| Orc | `orc_tusk`, `crude_iron` (+ `raw_meat`, `beast_pelt`) |
| Demon | `demon_horn` (rare), `ember_shard`, `demon_ember` (+ `coal`) |
| Blood monster | `blood_vial` (+ `raw_meat`) |
| Skeleton | `bone`, `bone_dust`, `bone_shard` |
| Golem | `stone_core` (rare) (+ `stone_chunk`, `iron_ore`) |
| Wolf | `wolf_pelt`, `wolf_fang` (+ `raw_meat`, `beast_pelt`) |
| Wraith | `ectoplasm`, `ash_essence` (rare) (+ `mana_crystal`) |
| Generic drops | `plant_fiber`, `rope`, `small_health_potion`, `rough_gem` |
| Potions | `small_health_potion`, `greater_health_potion`, `mana_tonic` |
| Tools/weapons | `wood_axe`, `stone_pickaxe`, `iron_axe`, `crystal_pickaxe`, `copper_sword`, `iron_sword`, `hunting_bow`, `bone_bow`, `crystal_bow`, `arcane_staff`, `shaman_staff`, `crystal_staff`, `ember_blade`, `demon_cleaver` |
| Armor/accessories | leather/iron sets, `crystal_crown`, `crystal_robe`, `orc_war_helm`, `abyssal_crown` (legendary), `slime_ring`, `blood_amulet` |
| Placeables/stations | wall/door/floor/pillar kits, `forge_anvil`, `workbench_kit`, `stove_kit`, `alchemy_table_kit` |

## 9. Server integration

- **Loading.** `server/Carbon.Host/Program.cs` calls `GameEconomy.FindConfigDirectory` and `GameEconomy.LoadFromDirectory` at boot. The Host copies `resources/config/*.json` to `config/` next to its binaries. A missing or invalid config fails the boot, so the server never runs with silent defaults.
- **Kills.** `AuthoritativeWorld.TryRollEconomyLootLocked` (`server/Carbon.Application/World/AuthoritativeWorld.Economy.cs`) replaces the hardcoded `MonsterArchetypeSpec` drops for every monster that has a loot table:
  - `WildSlime` → `slime`, `SkeletonArcher` → `skeleton`, `OrcShaman` → `orc`, `CrimsonDemon` → `demon`, `RockGolem` → `golem`, `FrostWolf` → `wolf`, `AshWraith` → `wraith`.
  - Item stacks become ground loot. Gold and diamonds go into the killer's `Wallet`, and the killer's `LootPity` counter is updated.
- **Crafting.** `PlayerInventoryLayout.TryCraftInBag`, which `TryCraftRecipe` uses, now runs through `RecipeMatcher` on a draft of the bag slots. It is all-or-nothing, never consumes forge-upgraded stacks, and never touches equipment slots.
- **Art.** Every item with id ≥ 100 has a `resources/art/items/<code>.cvg.json` icon registered in `catalog.cvg.json`.

**Not done yet** (these need a contracts change or a schema ADR):
- Wallet balances aren't persisted (`CharacterRecord` has no currency columns).
- Balances aren't synced to clients (there's no wallet packet yet).
- Vendor and repair handlers aren't exposed over the wire.

## 10. Changing the numbers

1. Edit the JSON. The loaders reject unknown properties, unknown item codes, duplicate ids and out-of-range values with an `InvalidDataException` that names the file and field.
2. Run `dotnet test tests/Carbon.Engine.Tests.slnx`. The simulation tests show the simulated gold/hour or diamonds/hour next to the design band it left.
3. If the design intent changed, update `designTargets` and the tables in this document in the same PR.

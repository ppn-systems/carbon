# Carbon Economy Model

How gold and diamonds enter and leave the world, how loot is rolled, and how crafting turns materials into value.
Every number below is in `resources/config/*.json`. The tests in `tests/engine/Carbon.Economy.Tests` check the configs against these numbers, so a balance regression fails CI.

| File | Owns |
| :--- | :--- |
| `resources/config/items.json` | Item ids, codes, rarity, tier, stack size, **base price**, durability, vendor flag, theme tags (nutrition lives in the survival agent's `food.json`) |
| `resources/config/loot.json` | Loot table per monster family / gather node |
| `resources/config/recipes.json` | Recipes: station, work ticks, skill/level gates, ingredients, outputs |
| `resources/config/economy.json` | Currency caps, gold/diamond curves, pity, vendor spread, crafting fees, design targets |
| `resources/config/repair.json` | Repair materials per family and tier, station tiers, blacksmith fee ([ADR-028](adr/ADR-028-material-repair-per-item.md)), durability warning shares ([ADR-035](adr/ADR-035-items-break-at-zero-durability.md)); synced to clients |

Code: `engine/Carbon.Economy` (wallet, loot, vendor, fees) and `engine/Carbon.Items` (item catalog, recipes, atomic crafting, crafting jobs).

## 1. Design principles

Proven survival/MMO economies (RuneScape, WoW, EVE, Diablo, Terraria, RimWorld) share a few rules:

1. **Faucets vs sinks.** Every source of currency (faucet) needs matching destruction (sink), or prices inflate and new players can't keep up. Our faucets are monster gold, vendor sales and diamonds. Our sinks are vendor purchases, the vendor buy/sell spread, repairs and station fees.
2. **Sub-linear income growth.** Gold per kill grows as `level^0.6`. Doubling the level gives about 1.5× the gold, not 2× (EVE/RuneScape-style damping). Gear prices also rise by tier, so repair and fee sinks grow with income.
3. **No arbitrage.** Vendors pay 25% of base price and charge 100%. Buying ingredients, crafting and selling always loses gold, and a test checks this for every recipe.
4. **Gray monsters pay little.** A monster more than 8 levels below the player is *trivial* (the WoW "gray mob" rule). It pays 20% gold, can't drop diamonds and doesn't advance pity, so farming starter zones is pointless.
5. **Premium currency stays rare.** Diamonds come mainly from elites (×5), bosses (×40), deep/distant zones (+25% per distance tier) and rich ore veins. Bad-luck protection puts a floor on income without making diamonds common.
6. **Server-authoritative and deterministic.** The server seeds a `DeterministicRandom` per kill (`LootRoller.CreateRandom(key, tick, sourceId)`, where `key` is the secret epoch subkey of [ADR-026](adr/ADR-026-server-secret-gameplay-entropy.md), never the public world seed), rolls and sends the result. Clients never roll. The same secret and inputs always give the same loot, so audits can be replayed.
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
| `InsufficientFunds`, `BalanceCapExceeded`, `InsufficientItems` | `Conflict` |
| `UnknownItem` | `NotFound` |
| `NotForSale` | `Forbidden` |
| `InventoryFull` | `InventoryFull` |

## 3. Loot (`Carbon.Economy.Loot`)

Each table in `loot.json` is keyed by source id. Every `lootTableId` in `monsters.json` has a table, and a test checks this, including the elite flag. The families are `slime`, `skeleton`, `orc`, `blood_monster`, `demon`, `golem`, `wolf` and `wraith`, plus the four `water` tables of ADR-032's water creatures (`crab`, `piranha_slime`, `jelly`, `eel`). The elite variants `slime_king` and `orc_warlord` are marked `"elite": true`, so they roll with elite multipliers and are excluded from the normal-monster income bands. There is also one gather node, `rich_ore_vein`.

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
| `crab` (water) | 1–100 | `raw_meat` 1 | `raw_meat`, `stone_chunk`, `small_health_potion` (3), `mana_crystal` (30), `slime_core` (45), `greater_health_potion` (60), `rough_gem` (60) |
| `piranha_slime` (water) | 10–100 | `slime_gel` 1–2 | `slime_gel`, `raw_meat`, `slime_jelly`, `slime_core` (20), `mana_crystal` (30), `greater_health_potion` (55), `rough_gem` (60) |
| `jelly` (water) | 10–100 | `slime_jelly` 1–2 | `slime_jelly`, `ectoplasm`, `mana_crystal`, `mana_tonic`, `slime_core` (35), `greater_health_potion` (55), `rough_gem` (60) |
| `eel` (water) | 25–100 | `raw_meat` 1–2 | `raw_meat`, `leather`, `slime_core` (45), `greater_health_potion` (50), `rough_gem` (60) |

The water tables span wide level ranges because the sea's level follows distance like the land's. The items they drop carry the `water` tag, and their stack sizes and level-gated valuables keep the per-level gold, net and sink-share bands of §7.1 green.

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

The design target is that sinks remove **35–60% of faucet gold from level 10 up** (`designTargets.sinkShare`). Levels 1–9 may be lower so new players can gear up. Every sink is data in `economy.json` and runs through the live server paths (`VendorService`, `CraftingCheckout`).

| Sink | Rule (`economy.json`) | Example |
| :--- | :--- | :--- |
| Vendor spread | Buy at 100% of base price; sell at `floor(25%)`. Vendors sell `vendorBuyable` basics (logs, stone, fiber, coal, wheat, berries, raw meat, cloth, tier-1 tools) and consumables (cooked meat, bread, stew, roast meat, arcane pie, health potions, mana tonic). | `orc_tusk` (15 g) sells for 3 g |
| Consumable upkeep | Consumables cost `vendor.consumablePriceBands[buyer level]` percent of the buy price: 100% at levels 1–14, rising in 5-level steps to 790% at 95–100. This tracks the level-scaled gold faucet. A band is never below 100%, so buy-then-sell never mints gold. | Small health potion: 12 g at level 10, 59 g at level 60 |
| Repair | Blacksmith fee (`repair.json → blacksmithFee`): `ceil(basePrice × 30% × rarity% × restored/max durability)`, minimum 1 g; rarity: common 50, uncommon 100, rare 175, epic 300, legendary 400. Every repair also spends materials, and a player workbench or anvil charges no gold (ADR-028). The held weapon or tool wears (1.5 per weapon strike, `ToolUsagePolicy`) and each equipped armor piece wears 0.5 per monster hit taken. An item that reaches 0 durability breaks and is destroyed ([ADR-035](adr/ADR-035-items-break-at-zero-durability.md)). | Half-worn, full repair: copper sword 5 g, iron sword 17 g (+ 2 iron ingots), ember blade 216 g |
| Station fee | `(flat + 10% of the outputs' base value) × rarity%` of the rarest output. Flat: hand 0, workbench 0, forge 2, stove 1, alchemy 5. `craftingFees.rarityMultiplierPercent`: common 100, uncommon 150, rare 250, epic 400, legendary 600. Hand crafting stays free. | Ember blade: (2 + 48) × 400% = 200 g; abyssal crown: (5 + 250) × 600% = 1,530 g |
| Death drop | From level 10 (`deathPenalty.minLevel`): `clamp(10% of carried gold, 2 × level, 30 × level)`, at most what is carried; diamonds never. The gold drops as 1–5 piles, reserved for the dead player for 120 s. Anyone else may take it after that, and it despawns after 300 s. Only unclaimed gold leaves the economy; ledger reason `DeathDrop` ([ADR-031](adr/ADR-031-player-death-state-respawn-and-gold-drop.md)). | Level 20 carrying 5,000 g drops 500 g; carrying 100 g drops 40 g |

`VendorService.TryBuy` checks funds, wallet cap and inventory space before committing both sides; the server passes the buyer's level. A full bag is never charged, and a capped wallet never loses the items it tried to sell. `CraftingCheckout` charges the station fee only when the craft (or job start) succeeds, and refuses the craft with `InsufficientFunds` before touching the inventory.

### 7.1 Player-hour balance simulation (`Carbon.Economy.Simulation`)

`EconomySimulator` plays the engaged player described in `economy.json → playerModel` for N hours at one level. It uses the real tables: `LootRoller` for loot, `PriceBook` sell prices, `ToolUsagePolicy` weapon wear, the blacksmith fee of a full repair (`RepairTable.TryQuoteFull`; materials are gathered, not bought), `VendorService.TryBuy`, and `PriceBook.GetCraftingFee`. Every gold movement goes through a `Wallet`, and the report is read back from the ledger by `CurrencyReason`.

Each simulated hour runs this model:

- **Kills:** `designTargets.killsPerHour` kills, rotating between the normal (non-elite) monster tables that cover the level.
- **Loot sales:** `sellLootPercent` of the looted item units are sold.
- **Weapon wear and repair:**
  - Each kill takes the stage's `strikesPerKill` strikes (monster HP over the stage weapon's damage): 3 at levels 1–24, 2 from level 25.
  - Each strike wears the stage's `weapon` by the server's per-strike loss.
  - The weapon is repaired at the blacksmith once it drops below `repairBelowPercent`.
- **Crafting fees:** `craftsPerHour` station fees are paid, cycling through the stage's recipes.
- **Consumables:** the stage's `consumables` are bought at the buyer's level-band price.

`EconomyBalanceSimulatorTests` runs 20 hours at 25 levels with seed 2026: every 5 levels plus each stage boundary. It prints the table below. It fails when any of these leaves its band:

- net gold/hour (`designTargets.netGoldPerHour`);
- the sink share (`designTargets.sinkShare`);
- the inflation slope, the least-squares slope of net gold/hour against level (`designTargets.inflationSlope`, 10–40).

**Before (#34) and after sink tuning**, per hour:

| Level | Faucets before | Sinks before | Share before | Faucets | Repairs | Station fees | Vendor buys | **Net/h** | **Sink share** | Net band |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| 1 | 89 | 52 | 58 % | 89 | 35 | 8 | 8 | **38** | 58 % | 0–900 |
| 5 | 364 | 52 | 14 % | 364 | 35 | 8 | 8 | **312** | 14 % | 0–900 |
| 10 | 624 | 72 | 12 % | 624 | 94 | 32 | 168 | **330** | 47 % | 200–1,200 |
| 20 | 1,124 | 72 | 6 % | 1,124 | 94 | 32 | 364 | **635** | 44 % | 200–1,200 |
| 24 | | | | 1,207 | 94 | 32 | 364 | **718** | 41 % | 200–1,200 |
| 30 | 1,514 | 82 | 5 % | 1,514 | 128 | 84 | 480 | **821** | 46 % | 450–2,200 |
| 40 | 1,824 | 82 | 5 % | 1,824 | 128 | 84 | 577 | **1,035** | 43 % | 450–2,200 |
| 49 | | | | 2,750 | 128 | 84 | 860 | **1,678** | 39 % | 450–2,200 |
| 50 | 2,795 | 116 | 4 % | 2,795 | 484 | 310 | 648 | **1,353** | 52 % | 1,000–3,000 |
| 60 | 3,033 | 116 | 4 % | 3,033 | 484 | 310 | 701 | **1,537** | 49 % | 1,000–3,000 |
| 74 | | | | 4,315 | 484 | 310 | 1,096 | **2,425** | 44 % | 1,000–3,000 |
| 75 | 4,273 | 258 | 6 % | 4,273 | 703 | 324 | 906 | **2,340** | 45 % | 1,600–3,600 |
| 100 | 4,937 | 258 | 5 % | 4,937 | 703 | 324 | 1,046 | **2,864** | 42 % | 1,600–3,600 |

From level 10 up the sink share is now 39–52% (it was 4–12%). The inflation slope fell from about 49 to about 28 net gold/hour per level, so a level-100 player keeps about 2,900 gold/hour instead of 4,700. The faucets are unchanged; only sinks moved. Within each stage the fixed sinks (repair, station fees) stay flat while income grows, so the share drifts down toward the end of each stage. The consumable price bands are stepped to keep it above 35%.

**With the water creatures (ADR-032 D14)**, the simulator also farms the water tables at each level they cover:

| Level | Faucets | Repairs | Station fees | Vendor buys | **Net/h** | **Sink share** | Net band |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- |
| 1 | 125 | 35 | 8 | 8 | **73** | 41 % | 0–900 |
| 10 | 764 | 94 | 32 | 168 | **471** | 38 % | 200–1,200 |
| 24 | 1,359 | 94 | 32 | 364 | **869** | 36 % | 200–1,200 |
| 49 | 2,669 | 128 | 84 | 860 | **1,597** | 40 % | 450–2,200 |
| 50 | 2,766 | 484 | 310 | 648 | **1,324** | 52 % | 1,000–3,000 |
| 75 | 3,774 | 703 | 324 | 906 | **1,840** | 51 % | 1,600–3,600 |
| 100 | 4,302 | 703 | 324 | 1,046 | **2,230** | 48 % | 1,600–3,600 |

The diamond column of the printed table has too few kills (2,400 per level) to be meaningful. The diamond band is gated by the 1,000,000-kill test in `EconomySimulationTests`.

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
| Tools/weapons | `wood_axe`, `wood_pickaxe`, `wood_sword`, `stone_axe`, `stone_pickaxe`, `iron_axe`, `crystal_pickaxe`, `copper_sword`, `iron_sword`, `hunting_bow`, `bone_bow`, `crystal_bow`, `arcane_staff`, `shaman_staff`, `crystal_staff`, `ember_blade`, `demon_cleaver` |
| Armor/accessories | leather/iron sets, `crystal_crown`, `crystal_robe`, `orc_war_helm`, `abyssal_crown` (legendary), `slime_ring`, `blood_amulet` |
| Placeables/stations | wall/door/floor/pillar kits, `forge_anvil`, `workbench_kit`, `stove_kit`, `alchemy_table_kit` |

**Tool tiers.** Wood tools (`wood_axe`, `wood_pickaxe`, `wood_sword`) are hand recipes from logs, planks, fiber and rope. They have harvest tier 1, low durability and a modest edge. Stone tools (`stone_axe`, `stone_pickaxe`) are made at a workbench: harvest tier 2, more durability and more damage. Copper and iron come next, at the forge.

## 9. Server integration

- **Loading.** The Host builds `GameEconomy.Load(ConfigRepository.Catalog)` from the same snapshot it serves to clients (ADR-014). A missing or invalid config fails the boot.
- **One source for item stats and recipes.** `items.json` holds combat, tool, armor and consumable stats: `equipSlot`, `harvestTier`, `baseDamage`, `critChance`, harvest damage, `armorDefense`, bonuses, heals, `attackRange`, projectile fields, `capabilities`, `material` (`wood` or `stone`) and a held weapon's `attackSpeedBonus` (0..1). `recipes.json` holds every recipe. Both are compiled into one immutable `ItemRules` instance (`engine/Carbon.Items/Catalog/ItemRules.cs`), and there is no process-wide installed table.
  - **Server.** `GameEconomy.ItemRules` is owned by `AuthoritativeWorld` and swapped whole with the economy on hot reload (`ApplyGameData`). `AuthService` reads the world's current rules when it computes a new character's stats.
  - **Desktop.** Each session owns a `SyncedItemCatalog` (`IWorldReadModel.Items`). It rebuilds its `Rules` after every verified config sync, so tooltips, the hotbar, the crafting window and client prediction use the server's bytes. Before the first sync it falls back to the build-time copy of the shipped `resources/config` embedded in `Carbon.Items`.
  - **Policies.** `ToolUsagePolicy`, `CharacterProgressionPolicy`, `PlayerInventoryLayout`, `ItemEnhancementPolicy` and `PropHarvestCatalog` take the `ItemRules` to evaluate against as a parameter. `StarterGameCatalog` keeps only the stable id constants and the starter loadout.
  - A guard test fails if desktop, graphics or `StarterGameCatalog` construct item or recipe definitions in code.
- **Kills.** `AuthoritativeWorld.TryRollEconomyLootLocked` (`server/Carbon.Application/World/AuthoritativeWorld.Economy.cs`) replaces the hardcoded `MonsterArchetypeSpec` drops for every monster that has a loot table:
  - `WildSlime` → `slime`, `SkeletonArcher` → `skeleton`, `OrcShaman` → `orc`, `CrimsonDemon` → `demon`, `RockGolem` → `golem`, `FrostWolf` → `wolf`, `AshWraith` → `wraith`.
  - Item stacks, plus a gold pile and a diamond pile when the roll gives any, become ground loot near the corpse, and the killer's `LootPity` counter is updated. Only the killer may pick the drops up for `economy.json → groundLoot.ownerProtectionSeconds` (60 s); after that anyone may. They despawn after `groundLoot.killLootLifetimeSeconds` (180 s). Picking up a pile credits the picker's `Wallet` ([ADR-019](adr/ADR-019-ground-currency-pickups-and-kill-loot-protection.md)).
- **Crafting.** `TryCraftRecipe` runs `recipes.json` recipes through `CraftingCheckout`, on a draft of the bag slots only. Each craft is all-or-nothing, never consumes forge-upgraded stacks, and charges the station fee.
  - **Station:** must be within 5 tiles.

    | Recipe station | Satisfied by |
    | :--- | :--- |
    | hand | anywhere |
    | workbench | a built Workbench |
    | stove | a Stove or Campfire |
    | forge | a ForgeAnvil or a forge-artisan NPC (the sanctuary blacksmith) |
    | alchemy | a Shrine Keeper NPC |

    Otherwise the craft fails with `OutOfRange`.
  - **Level gate:** the character level must meet `unlockLevel`, otherwise the craft fails with `Forbidden`. There are no crafting skills yet, so the character level also stands in for `skillLevel`.
  - **Alchemy is a deliberate exploration gate.** Potions, staves, rings, amulets and the abyssal crown need a Shrine Keeper. Shrine Keepers spawn at mountain shrines, never at the starting sanctuary, so players have to travel before they can craft alchemy items. New mages get their starter staff from the class loadout.
  - **Prop harvesting** (`PropHarvestCatalog`) switches on `MapPropType` and drops `items.json` items:

    | Prop | Drop |
    | :--- | :--- |
    | trees | `wood_log` |
    | boulder, basalt | `stone_chunk` |
    | coal deposit | `coal` |
    | copper vein | `copper_ore` |
    | iron and silver veins | `iron_ore` |
    | gold vein | `rough_gem` |
    | crystals | `mana_crystal` |
    | cactus | `cactus_fiber` |
    | bush | `berries` |
    | mushroom cluster | `berry_herb` |
    | dry bush, dune grass | `plant_fiber` |
    | ancient shrine | nothing |

    A test fails CI when a new prop type has no themed drop.
- **Art.** Every item with id ≥ 100 has a `resources/art/items/<code>.cvg.json` icon registered in `catalog.cvg.json`.

**Wallet persistence, sync and sinks** ([ADR-012](adr/ADR-012-server-authoritative-wallet-persistence-and-sync.md)):
- `CharacterRecord` persists `Gold`, `Diamonds`, `WalletSequence` and `DiamondPity` through migration `0005_character_wallet`, in both SQLite and PostgreSQL.
- `WalletSync` (S2C `0x1237`) pushes balances on world entry, after a gold or diamond pile is picked up and after vendor operations. The inventory window shows them in its header (not the main HUD, owner request; the trader window shows gold for prices), using `resources/art/ui/coin.cvg.json` and `diamond.cvg.json`.
- `VendorBuyItem` (`0x4007`) and `VendorSellItem` (`0x4008`) require a friendly town NPC within 175 px. Both reply with `MutationResult`. Repair is `RepairItemRequest` (`0x4050`, ADR-028), answered with `RepairItemResult` (`0x4051`).

## 10. Operator guide: changing the numbers

### 10.1 What to edit

| Goal | Edit | Watch in the table |
| :--- | :--- | :--- |
| More or less gold income | `economy.json → gold` (`levelExponent`, `dropChancePercent`), or `goldBase`/`goldMax` per table in `loot.json` | Loot gold, net/h |
| Item drop rates | `loot.json` entries (`weight`, `nothingWeight`, `min`/`max` quantities, `minLevel`) | Vendor sales |
| Repair pressure | `repair.json → blacksmithFee` (`percentOfBasePrice`, `rarityPercent`) | Repairs |
| Station fees | `craftingFees` flat fees, `outputValueRateBps`, `rarityMultiplierPercent` | Station fees |
| Consumable upkeep | `vendor.consumablePriceBands`, and `vendorBuyable` in `items.json` | Vendor buys |
| Item value | `basePrice` in `items.json`. This moves sell price, repair cost and station fees at once. | All sinks |

Rules the loaders enforce:
- **Unknown fields fail.** Unknown properties, unknown item codes, duplicate ids and out-of-range values fail with an `InvalidDataException` that names the file and field. A typo never loads silently.
- **No arbitrage.** `vendor.sellRatioPercent` must stay below `buyMarkupPercent`, and every consumable price band must be at least 100%, so buy-then-sell can never mint gold.
- **No overlapping bands.** Consumable price bands must not overlap.

### 10.2 Applying a change

- **Restart or hot reload.** `economy.json`, `loot.json`, `items.json` and `recipes.json` hot-reload on a running server (ADR-014). The reload is atomic: a file that fails validation rejects the whole reload, and the old tables keep running. Changes to `structures.json`, `monsters.json` and `spawning.json` need a restart.
- **Visibility to clients.** `economy.json` and `loot.json` are server-only, so clients never see prices or drop rates. `items.json` and `recipes.json` re-sync to clients.

### 10.3 Running the balance simulator

```bash
export PATH=$HOME/.dotnet:$PATH
dotnet test tests/engine/Carbon.Economy.Tests --filter "FullyQualifiedName~EconomyBalanceSimulatorTests" --logger "console;verbosity=detailed"
```

This prints the §7.1 table for 25 levels, plus the inflation slope. The run is deterministic (seed 2026), so two runs on the same config print the same numbers, and any difference comes from your edit.

To check a change:
1. Run the simulator before your edit.
2. Run it again after.
3. Compare the two tables.

Use the whole suite, `dotnet test tests/engine/Carbon.Economy.Tests`, before opening a PR. It also checks:
- gold/hour and diamond/hour per monster table (`EconomySimulationTests`, including the 1,000,000-kill diamond run);
- config integrity: every code resolves and loot stays on theme;
- the exact repair, fee and price rules.

### 10.4 Reading a CI failure

Each balance assertion names the metric, the level and the band. For example:

```
Expected r.SinkShare to be between 0.35 and 0.6 because sink share at level 24, but found 0.3412875...
Expected r.Net to be between 200 and 1200 because net gold/hour at level 20, but found 1312.25.
```

| Failure | Usual cause | Fix |
| :--- | :--- | :--- |
| Sink share below the band | Faucet raised, or a sink cut | Raise the consumable band for that level, or rarity percents for that stage's gear |
| Sink share above the band | Sink raised too far | Lower it; levels 1–9 may go up to 80% |
| Net gold/h out of band | Faucet change, or a big sink change | Check the faucet columns first |
| Inflation slope out of 10–40 | High levels earn too fast, or too slowly, relative to low levels | Adjust `gold.levelExponent` or the high-level price bands |
| `Repairs` or `Vendor buys` is 0 at some level | A stage's weapon does not wear, or a consumable is not `vendorBuyable` | Fix `playerModel.stages` |
| A gold/hour or diamond/hour band test fails for one table | A `loot.json` edit for that table | Adjust that table, not the global curve |

### 10.5 When the design intent changes

When the design changes, rather than an accidental regression:
- update `designTargets` (`goldPerHour`, `diamondsPerHour`, `netGoldPerHour`, `sinkShare`, `inflationSlope`);
- update `playerModel` if the expected player behaviour changed;
- update the tables in §4, §5 and §7 of this document;

all in the same PR. Never widen a band just to get CI green without stating why in the PR.

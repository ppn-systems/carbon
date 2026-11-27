# Forge Enhancement (+0..+10)

Players "smash" weapons, tools and armor at a forge (the Master Blacksmith or a placed Forge Anvil) to raise
`ItemSlot.UpgradeLevel`. Each level adds **+12 % stats** and **+10 % max durability**
(`ItemEnhancementPolicy.GetStatMultiplier` / `GetEffectiveMaxDurability`, unchanged). All attempt rules live in
[`resources/config/enhancement.json`](../resources/config/enhancement.json); the numbers below are the shipped defaults.
Wire and persistence decisions are in [ADR-020](adr/ADR-020-forge-enhancement-outcomes.md).

## Evaluation of the previous forge

| Before | Problem |
| :--- | :--- |
| Chances and materials hardcoded in `ItemEnhancementPolicy.CanUpgrade` (1.0 / 0.85 / 0.70) | Not data-driven; the desktop read the same compiled table (audit SA-1) |
| Only success or "materials lost" | No risk, so +10 was a matter of time; no reason to buy protection |
| `Random.Shared` roll unless a test seed was passed | Not replayable; not the deterministic stream the rest of the server uses |
| No gold cost | The best gear had no gold sink at all |
| Reply was a bare `MutationResult` (`Ok` / `EnhancementFailed`) | The client could not tell what happened, so it could not show it |

## Outcomes

Every attempt rolls one of four outcomes on the server. Weights are per mille and each row sums to 1000.

| Attempt | Success | No change | Drop −1 | Destroyed | Materials | Gold (common) |
| :--- | ---: | ---: | ---: | ---: | :--- | ---: |
| +0 → +1 | 100 % | 0 | 0 | 0 | 2 stone, 1 iron ore | 20 |
| +1 → +2 | 95 % | 5 % | 0 | 0 | 3 stone, 2 iron ore | 40 |
| +2 → +3 | 90 % | 10 % | 0 | 0 | 4 stone, 3 iron ore | 70 |
| +3 → +4 | 80 % | 20 % | 0 | 0 | 3 iron ore, 1 mana crystal | 120 |
| +4 → +5 | 70 % | 30 % | 0 | 0 | 4 iron ore, 2 mana crystal | 200 |
| +5 → +6 | 60 % | 25 % | 15 % | 0 | 5 iron ore, 3 mana crystal | 320 |
| +6 → +7 | 50 % | 28 % | 22 % | 0 | 2 iron ingot, 4 mana crystal | 500 |
| +7 → +8 | 40 % | 30 % | 30 % | 0 | 3 iron ingot, 5 mana crystal | 800 |
| +8 → +9 | 30 % | 30 % | 30 % | 10 % | 4 iron ingot, 6 mana crystal, 1 rough gem | 1,200 |
| +9 → +10 | 20 % | 30 % | 35 % | 15 % | 5 iron ingot, 8 mana crystal, 2 rough gem | 2,000 |

- **Safe levels.** Attempts from **+0..+4** never lose anything (`downgradeMinLevel = 5`). Items can only break
  from **+8** (`destroyMinLevel = 8`). The loader rejects a row that breaks either rule.
- **Gold fee** = row gold × rarity percent: common 100, uncommon 150, rare 250, epic 400, legendary 700.
  It is charged on every attempt, whatever the outcome.
- **Durability.** A success restores the item to its new maximum. A downgrade only caps durability at the lower maximum.
- A **destroyed** item is removed from its slot.

## Protection scrolls (economy sinks)

| Scroll | Effect | Source | Base price |
| :--- | :--- | :--- | ---: |
| Forge Ward Scroll (`forge_ward_scroll`, 205) | A downgrade becomes no change. Destruction still happens. | Vendors (gold) | 90 |
| Blessed Forge Scroll (`blessed_forge_scroll`, 206) | A downgrade or destruction becomes no change. | Alchemy table: 1 ward + 3 mana crystal + 1 rough gem | 420 |

One scroll is spent per attempt it is used on, whatever the outcome. A scroll is refused (nothing spent) on a level
where it cannot change anything (+0..+4, or a ward where only destruction is possible). Scrolls never change the success
chance. Both have vector art (`resources/art/items/*_scroll.cvg.json`).

## Expected cost to reach a level

Monte Carlo of the shipped table, common item starting at +0 (`ForgeEnhancerTests.MonteCarlo_ExpectedCostToReachLevel_MatchesTheDocumentedTable`
checks these within 6 %). Without protection a destroyed item restarts at +0 with a new item. "Blessed" uses a
blessed scroll on every risky attempt (+5 and up). Multiply gold by the rarity percent; materials follow from the
attempt count and the table above.

| Target | Attempts | Gold | Items destroyed | Attempts (blessed) | Gold (blessed) | Blessed scrolls |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| +5 | 5.8 | 576 | 0 | 5.8 | 576 | 0 |
| +7 | 10.7 | 2,443 | 0 | 9.5 | 2,109 | 3.7 |
| +8 | 15.4 | 5,404 | 0 | 12.0 | 4,095 | 6.2 |
| +9 | 28.4 | 14,075 | 0.33 | 15.3 | 8,111 | 9.5 |
| +10 | 78.4 | 50,358 | 1.93 | 20.3 | 18,049 | 14.5 |

Against the net gold/hour bands in [economy.md](economy.md) (about 300/h at level 10, 1,000–1,700/h at 40–50,
2,900/h at 100): +5 is an evening for a new player, +7 a few hours, and +10 on an epic item (×4) a long-term goal that
removes 70,000–200,000 gold. Enhancement is opt-in, so it is not part of the `EconomySimulator` sink model.

## Server flow (ADR-017 tick queue)

1. `ItemHandlers.HandleUpgradeInventoryItemAsync` (opcode `0x4005`) reads `SlotIndex` and `ProtectionItemId` and
   queues `WorldCommandKind.UpgradeInventoryItem`. The request has no field that could choose or seed the outcome.
2. On the next tick `AuthoritativeWorld.TryEnhanceInventoryItem` checks forge reach, then `ForgeEnhancer.TryEnhance`
   checks the item, the level, every material, the scroll and the gold **before spending anything**. It then spends the
   gold fee (`CurrencyReason.Enhancement`), the materials and the scroll once, and rolls on
   `ForgeEnhancer.CreateRandom(worldSeed, tick, player, attemptNonce)`.
3. The outcome is written to the slot under the world lock, the character is marked dirty (inventory and wallet are
   persisted together), and the owner gets `InventorySlotChanged`, `PlayerStatsSync` and (with the tick dispatch) `WalletSync`. Nearby players
   get the new look when equipped gear changed.
4. The handler replies `EnhancementResult` (`0x4031`): status, outcome, previous and new level, destroyed, scroll spent.
5. A success that reaches **+7 or more**, or a destroyed item, sends `EnhancementAnnouncement` (`0x4032`) to every
   player in the world (`announce` in the config).

## Desktop

The forge window (`ForgeAnvilModalWidget`, key `U`) reads the synced `enhancement.json` through
`IWorldReadModel.Items.Rules.Enhancement`: materials with owned counts, the gold fee, and the four chances as numbers
and as one stacked bar. It never rolls. Outcome visuals (hammer charge-up and sparks at the anvil, then a glow burst,
dull clank, cracks with a red flash, or shatter particles) are driven by `IWorldReadModel.ForgeResultSerial` /
`LastForgeOutcome`, the server's result.

## Tuning

Edit `resources/config/enhancement.json`. The server hot-reloads it (ADR-014) and clients re-sync it, so the forge
window shows the new chances immediately. After changing weights or gold, rerun the Monte Carlo test and update the
table above.

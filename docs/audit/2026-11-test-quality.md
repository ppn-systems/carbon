# Test-Quality Audit — November 2026

| Field | Value |
| :--- | :--- |
| **Audited commit** | `a64a98f` (master, PR #109) |
| **Scope** | `tests/**` (205 files, ~30k lines), plus the production code those tests are meant to protect |
| **Question** | Can each test fail for a real bug? Which risky behaviour has no test at all? |
| **Method** | Read every test file. Check each suspicious test against the code under test. Mine `git log -p` on the guard files for evidence of cosmetic gaming. Measure client methods with a Roslyn prototype (cyclomatic complexity, nesting, lines). For every test that was added or rewritten, break the code under test, run the test, record the result and revert. The results are in each fix PR. |
| **Status** | Report plus fixes. Each finding's **Fix** column names the PR branch that addresses it. Findings marked *report only* are known debt and are not changed here. |

## How to read this

- IDs are `TQ-<category>.<n>`. `file:line` refers to `a64a98f`.
- **Tautology:** the test recomputes the production formula, or reads the same config value that the code reads. A bug in the formula is then copied into the expectation, so the test cannot disagree with the code.
- **Mutation-checked:** for each fixed test, its PR lists the code change that makes it fail.
- Not every suspicious pattern is a defect. Where a pattern is acceptable, the report says so, so nobody "fixes" it later.

## Executive summary

1. **The riskiest client code had no test.** #109 moved about 300 lines of inventory input (drag/drop, split, drop, destroy, hotbar bind) into `InventoryInputController`. The only test change was a line-count number. The class could not be unit-tested, because it took the concrete GL-backed `HudOverlayRenderer`. **Fixed:** an 8-member `IInventoryInputHud` seam plus 39 behaviour tests. All 10 mutants were killed (`test/inventory-input-controller`).
2. **Line caps measure layout, not risk, and they are being gamed.** The commit log shows folds, partial merges and API moves whose stated goal was to fit under a cap (TQ-7.1). The guards also miss real complexity. `DesktopOptions.FromEnvironment` (cyclomatic complexity 35) and `ItemTooltipBuilder.Build` (34) pass the 120-line method cap, while straight-line draw code is flagged. **Recommendation, implemented:** cap cyclomatic complexity per method with an exact-pinned allowlist that can only shrink, and keep line counts only as a loose backstop (`test/complexity-guards`).
3. **Allowlists record the status quo.** Several "only shrinks" allowlists grew, re-keyed when code moved, or were pinned with headroom instead of at the measured value (TQ-7.2–7.5).
4. **A handful of tautologies and output-only tests.** Economy pricing tests re-implement `PriceBook`'s formulas. Two "tests" assert nothing about behaviour (a printed table and a wall-clock benchmark). **Fixed** in `test/tautology-cleanup`.
5. **Risky untested paths on the server:** player death and respawn, multi-observer delta dispatch, a trader-stock save failure, and progression hot-reload with players online (TQ-8). **Tests added** for the top paths in `test/risky-path-coverage`.

Over-mocking is **not** a problem in this suite. NSubstitute is used only at leaf seams, and the assertions target the real system under test.

---

## 1. Tautologies

| ID | Location | Test | Why it is low-value | Fix |
| :--- | :--- | :--- | :--- | :--- |
| TQ-1.1 | `tests/engine/Carbon.Economy.Tests/VendorAndCheckoutTests.cs:149` (formula at :159) | `GetRepairCost_ScalesWithRarity` | Re-implements `ceil(base × 0.30 × missing × pct / 100)`. It also hardcodes `0.30`, which production reads from `economy.json` (`PriceBook.cs:100-103`), and it ignores `MinimumCost`. So it is a copy of the code that also breaks on tuning. | rewritten: `test/tautology-cleanup` |
| TQ-1.2 | `VendorAndCheckoutTests.cs:168` (:172-174) | `GetCraftingFee_ScalesWithRarestOutput` | Recomputes `(Forge + value × bps / 10000) × rarity / 100`, the same expression as `PriceBook.cs:172-173`. | rewritten: `test/tautology-cleanup` |
| TQ-1.3 | `VendorAndCheckoutTests.cs:179` (:186-187) | `GetBuyPrice_Consumables_ArePricedByBuyerLevelBand` | The level-60 expectation is the production formula. The relational asserts in the same test are good and are kept. | rewritten: `test/tautology-cleanup` |
| TQ-1.4 | `tests/engine/Carbon.Items.Tests/ProgressionConfigTests.cs:170` (:175) | `Mitigate_DefenseCurve_MatchesFormula…` | `expected = 100 / (100 + K + perLevel × 10)` copies the curve. The diminishing-returns checks are good. | formula line replaced by the curve's half-mitigation property: `test/tautology-cleanup` |
| TQ-1.5 | `ProgressionConfigTests.cs:233` | `ComputeBaseKillExperience_Formula_…` | Every expectation is `round(hp × MaxHealthFactor + LevelFactor)` and similar, i.e. the production formula built from the same config. | rewritten as monotonicity, clamp and multiplier-direction properties: `test/tautology-cleanup` |
| TQ-1.6 | `tests/engine/Carbon.Items.Tests/TimeToLevelSimulation.cs:37, :40` | (balance-model helper) | Copies server constants as literals: the 0.5 s attack interval (`PlayerActionService.cs:269`) and 250 walk speed (`AuthoritativeWorld.cs:60`). The model silently drifts from the server when either changes. | report only (balance tool, not a correctness test) |
| TQ-1.7 | `tests/engine/Carbon.Items.Tests/EnhancementTableTests.cs:41` (:43-44) | `ShippedTable_SafeLevels_NeverDowngradeOrDestroy` | Pins `DowngradeMinLevel == 5` and `DestroyMinLevel == 8`, copied from `enhancement.json`. The behavioural half of the test (no downgrade below the threshold) is good. | report only |
| TQ-1.8 | `tests/engine/Carbon.Items.Tests/CharacterProgressionCodecCacheTests.cs:17` (oracle at :91) | `Serialize_…MatchesTheDocumentedLayout` | The oracle is a second hand-written encoder that calls production `CopyNonZeroAttributes`/`CopyLearnedSkills` for ordering. A literal byte array would pin the wire format. | report only |
| — | `tests/server/Carbon.Host.Tests/CharacterProgressionServiceTests.cs:155`, `CombatServiceTests.cs:79` | `ResolveIncomingDamage_ManyHits_…`, `ApplyMonsterHits_ArmoredMonster_…` | They use the config's `Mitigate` as the oracle. **Acceptable:** these are integration tests that the *service* routes through the shared formula, and the formula has its own tests. Do not rewrite. | none needed |

## 2. No meaningful assertion

| ID | Location | Test | Why | Fix |
| :--- | :--- | :--- | :--- | :--- |
| TQ-2.1 | `tests/engine/Carbon.Items.Tests/TimeToLevelSimulationTests.cs:85` | `Simulate_Report_PrintsTable` | Builds a balance table into a `StringBuilder` and asserts nothing. It is a printout, not a test, and it runs about 90 simulations on every CI run. | deleted (the other tests in the file assert the targets): `test/tautology-cleanup` |
| TQ-2.2 | `tests/desktop/Carbon.Desktop.Tests/TerrainBakeTests.cs:92` | `BakeChunkGeometry_RepresentativeBiomes_ReportsBakeCost` | A wall-clock benchmark: 40 warm-up passes plus 25 timed bakes per biome, timed with `Stopwatch`. Its only assertion is `bakes > 0`. It violates the no-wall-clock rule, costs CI time, and cannot catch a bug. | deleted: `test/tautology-cleanup` |
| TQ-2.3 | `tests/architecture/Carbon.Architecture.Tests/DamageMitigationTests.cs:99` | `ServerDamagePaths_ResolveThroughCombatDamage` | Passes if the text `CombatDamage.TryResolve(` appears in two files. Dead or unreachable code passes. | report only (the behaviour is covered by `CombatServiceTests`) |

No `Assert.True(true)` tests were found. No tests assert only "does not throw", except the strict-JSON parse check at `ResourcesConfigTests.cs:33`, which is legitimate.

## 3. Over-mocked tests

None found. NSubstitute is used only at leaf seams (`ITexture`, `IFocusable`, stores), and the assertions are on the real system under test (`SpriteAtlasTests`, `FocusManagerTests`, `AuthServiceDebugStarterItemsTests`).

## 4. Duplicates

| ID | Location | Duplicates | Why | Fix |
| :--- | :--- | :--- | :--- | :--- |
| TQ-4.1 | `tests/contracts/Carbon.Contracts.Tests/MapOpCodeCoverageTests.cs:22, :33, :46` | `ProtocolContractGuardTests.cs:48, :98` | The opcode band and uniqueness checks repeat the generic guard. The test at `:46` is named `…IsUsedByExactlyOnePacket` but only checks `Contains`, i.e. at least one. | `:46` made to assert exactly one: `test/tautology-cleanup`. The rest is report only. |
| TQ-4.2 | `tests/server/Carbon.Host.Tests/CombatServiceTests.cs:105` | same file `:143` | Same setup (wall hit, one mob). One test asserts the tick, the other HP. | report only |
| TQ-4.3 | `tests/engine/Carbon.Combat.Tests/CombatModuleTests.cs:187` | `:221`, `:235`; `CombatServiceTests.cs:65` | "Defense 100 halves 40 → 20" and "invulnerable takes 0" are asserted three or four times across layers. | report only |
| TQ-4.4 | `tests/architecture/Carbon.Architecture.Tests/LocaleFileStructureTests.cs:19` | same file `:49`; `IniDocumentTests.cs:82` | Re-implements duplicate-section detection, although the real `IniDocument.Load` already rejects duplicates. | report only |
| TQ-4.5 | `tests/server/Carbon.Host.Tests/ServerLayerArchitectureTests.cs:18` | `LayerDependencyTests.cs:84, :106` | The server → Graphics/Desktop/Silk.NET bans are already enforced generically. | report only |
| TQ-4.6 | `tests/desktop/Carbon.Desktop.Tests/VectorArtAndArchitectureTests.cs:119` (:188-209) | `IniDocumentTests.cs:153` | The `UiSettings` round-trip is tested twice, the first time inside an unrelated architecture mega-test. | report only |

## 5. Coupled to implementation details or source text

| ID | Location | Why | Fix |
| :--- | :--- | :--- | :--- |
| TQ-5.1 | `tests/desktop/Carbon.Desktop.Tests/VectorArtAndArchitectureTests.cs:347` (:364-366) | Greps `CraftingModalWidget.cs` for method names (`CanCraftFromSpan`, `CountBagItem`). A rename evades it, and a correct refactor breaks it. | report only |
| TQ-5.2 | `tests/desktop/Carbon.Desktop.Tests/SyncedItemCatalogTests.cs:91` | Substring grep for `"new ItemArchetype("`. Target-typed `new(...)` evades it. A semantic check (Roslyn symbol lookup) would hold. | report only |
| TQ-5.3 | `DamageMitigationTests.cs:76` (regex :121) | The regex `100f\s*/\s*\(\s*100f\s*\+` is evaded by `100.0f` or a named constant. | report only |
| TQ-5.4 | `VectorArtAndArchitectureTests.cs:119` (:178-185) | Asserts that `script/` contains exactly two file names. This is repo-layout trivia inside a desktop test. | report only |
| — | `CharacterProgressionCodecCacheTests.cs:42`, `LocalizerTests.cs:160` | `BeSameAs` asserts cache identity. **Acceptable:** these are deliberate zero-allocation contracts (AGENTS.md rule 4). | none needed |

## 6. Fragile magic numbers

| ID | Location | Why | Fix |
| :--- | :--- | :--- | :--- |
| TQ-6.1 | `tests/engine/Carbon.Economy.Tests/ForgeEnhancerTests.cs:194-201` | Pins seven Monte-Carlo means (e.g. +10: 78.4 attempts, 50,358 gold) at ±6%. Any `enhancement.json` tuning breaks it. It is intended as a balance lock, so it should live with the balance doc and say so. | report only |
| TQ-6.2 | `tests/engine/Carbon.Items.Tests/TimeToLevelSimulationTests.cs:17-28, :37-44` | Undocumented minute targets (3, 6, 9.5 … 147; 2,200 total). They are balance targets, not correctness checks. | report only |
| TQ-6.3 | `ProgressionConfigTests.cs:76-84` | Exact stat points per level, copied from `progression.json`. | report only |
| TQ-6.4 | `VendorAndCheckoutTests.cs:116`, `:86` | `expected.Should().Be(17)` ("110g sword") and `15 × SellRatio` hardcode catalog prices. | report only |
| TQ-6.5 | `ProgressionConfigTests.cs:316`; `EnhancementTableTests.cs:140`; `tests/server/Carbon.Host.Tests/ConfigHotReloadDomainsTests.cs:70, :84` | Text-replace exact lines of the shipped JSON (`"baseGold": 50,`), so retuning fails them. They also accept any `InvalidDataException`, so the rejection reason is never checked. | report only |
| TQ-6.6 | `tests/server/Carbon.Host.Tests/SettlementNpcSpawnerTests.cs:60` | `AggroRadius == 192f` mirrors the literal `MathF.Max(192f, …)` at `SettlementNpcSpawner.cs:366`. | report only |

## 7. Proxy guards (line caps and growing allowlists)

### TQ-7.1 Line caps reward cosmetic edits, and the history shows it

`ClassSizeTests` caps `AuthoritativeWorld*.cs` at 8 files and 3,040 raw lines (currently 3,027). `HotPathGuardTests` caps client classes at 600 lines and client method bodies at 120 lines, with 12 class and 25 method allowlist entries.

A raw line count is lowered by joining lines, moving comments to line ends, merging files or moving code into tests. None of those reduce risk. The commit log shows all four, each made explicitly to fit a cap:

| Commit | Change made to fit the cap |
| :--- | :--- |
| `413ae07` (#61) | "fold the two smallest AuthoritativeWorld partials (partial cap 10 → 8)". The 26 lines saved were file headers, usings and namespaces; no logic left the class. |
| `dcd9c4b` (#75) | Moved the production shorthand `TryUpgradeInventoryItem` into `tests/server/Carbon.Host.Tests/ForgeWorldTestExtensions.cs` "so AuthoritativeWorld stays within the lowered cap (3038/3040)". |
| `83420e5` (#90) | "AuthoritativeWorld back under the lowered line cap: compact service construction…". At `AuthoritativeWorld.cs:855` a field now carries an end-of-line comment, while its neighbours at :853-854 keep the comment above. |
| `f906a02` (#86) | "DebugSpawnResolver keeps AuthoritativeWorld under its size cap". |
| `1b83e59` (#71) | Landed at exactly 3,040 of 3,040. |

Lines over 200 characters in the `AuthoritativeWorld` partials grew from 3 to 6, e.g. `AuthoritativeWorld.cs:233` (230 chars) and `:260`, next to one-argument-per-line constructors at :236-257.

The caps also **miss** real complexity. A Roslyn prototype over `desktop/` and `graphics/` (1,511 member bodies) found:

| Method | Cyclomatic complexity | Lines | Line guard result |
| :--- | ---: | ---: | :--- |
| `DesktopOptions.FromEnvironment` | 35 | 115 | passes the 120-line cap |
| `ItemTooltipBuilder.Build` | 34 | 97 | passes |
| `WorldSceneRenderer.CollectVisibleObjects` | 38 | 105 | passes |
| `ForgeAnvilModalWidget.DrawSkin` | 30 | 74 | passes |
| `CharacterSelectScene.Render` | 53 | 288 | allowlisted |
| `PointOfInterestRenderer.DrawHouse` | 36 | 185 | allowlisted. Much of it is straight-line drawing. |

### TQ-7.2 Allowlists are pinned with headroom, then re-keyed when code moves

- In `0a2fd96` (#84, which created the guard), caps were raised before merge: `MultiplayerSession` went 3,950 → 4,025 → 4,123, `WorldGameplayScene` 950 → 1,113, `Update` 500 → 504.
- In `a64a98f` (#109) the message says "the HandleInventoryTabInput method allowlist entry is gone". The diff actually renames it to `InventoryInputController.HandleTabInput = 148`. The debt moved; it was not paid.
- Many caps are "today + headroom" (e.g. 140 for 133, 650 for 625). The headroom is a free budget to grow, and the stale-entry tests cannot see it.

### TQ-7.3 `LocalizationLiteralAllowlist.txt` grew

`a9988c9` (#87) added 40 entries ("49 pre-existing literals it had missed join the allowlist"). Entries are keyed by path and text, so once a literal is allowed in a file, any number of new uses of it in that file pass.

### TQ-7.4 `ServerAuthorityTests` allowlist grows when code moves

Entries are keyed by `file|Type`. `4b478ab` added `ItemTooltipBuilder.cs|ItemEnhancementPolicy` and `c9f97f1` added `InventoryDialogRenderer.cs|ItemEnhancementPolicy`. `MonsterAttackAnimator` is labelled "NEW after the guard was written".

### TQ-7.5 Static-state allowlists

`ClientStaticStateTests.cs:17` and `CodeHygieneTests.cs:42`: `0a2fd96` added `SyncedProgressionConfig.s_current` mid-PR as "a new ambient global". `590a62b` correctly removed 3 entries.

### TQ-7.6 The LINQ guard measures imports, not allocations

`HotPathGuardTests.cs:22-44` bans `using System.Linq` across all of `engine/`, cold config loaders included. The real allocation risk is measured by `ServerTickAllocationTests.cs:28` (≤ 32 B/tick), which is the better guard. **Report only:** the LINQ ban is cheap and correct in direction.

### Proposal: measure complexity, not lines (TQ-7.1, TQ-7.2)

**Recommendation: replace the per-method line cap with a per-method cyclomatic complexity cap (Roslyn), pinned exactly, with a ratchet. Keep line counts only as a loose backstop.**

| Option | Gamed by folding or comments? | Catches dense branching? | Flags long straight-line draw code? | Cost |
| :--- | :--- | :--- | :--- | :--- |
| Raw line caps (today) | yes | no | yes (false positives) | — |
| Non-blank, non-comment line count | partly (folding still works) | no | yes | small |
| **Cyclomatic complexity per method** (1 + `if`/loop/`case`/switch arm/`catch`/`?:`/`&&`/`||`/`??`/`when`) | **no**: formatting does not change the syntax tree | **yes** | no | small: the guard already parses every file with Roslyn |
| Complexity plus nesting depth | no | yes | no | small. Depth > 4 only adds 11 methods that are already high-complexity, so it adds little signal for its cost. |

Design, implemented in `test/complexity-guards`:

1. `ClientMethods_ShouldNotExceedComplexityCap`: complexity ≤ **25** per member body in `desktop/` and `graphics/`. This is a common industry threshold, and at it 22 methods are over today.
2. **The allowlist is pinned at the measured value, not with headroom.** `ComplexityAllowlist_ShouldMatchMeasuredValues` fails both when a method grows past its entry and when it shrinks below it. The test tells the author to lower the entry to the measured value. So the list can only shrink, and it never carries hidden budget. An entry whose method drops to ≤ 25 must be removed.
3. **Line caps become backstops** at 300 lines per method and 1,500 per class, roughly 2.5 times the old caps. Each backstop allowlist holds only the extreme outliers, pinned exactly: `WorldGameplayScene.Update` and `MultiplayerSession`. Folding a line no longer buys anything that matters.
4. `AuthoritativeWorld` (`ClassSizeTests`):
   - It counts `partial class AuthoritativeWorld` **declarations** anywhere under `server/`, which closes the "file name or subfolder" glob loophole.
   - It caps the class's **total complexity**, pinned exactly with the same ratchet.
   - The 3,040-line cap becomes a 3,600-line backstop.

What this deliberately does not do: it does not add a new framework or analyzer package. Roslyn is already referenced by the architecture tests.

## 8. Important behaviour with no test (ranked by risk)

| Rank | Path | What can go wrong | Why existing tests miss it | Fix |
| ---: | :--- | :--- | :--- | :--- |
| 1 | `desktop/Carbon.Desktop/Scenes/InventoryInputController.cs:37` `HandleTabInput`, `:191` `RequestDrop`, `:262` `TryBindHoveredSlotToHotbar` | Dragging out of the window drops the whole stack. Only rare, enhanced or equipped items ask first. A wrong branch silently throws away items, splits the wrong slot, or destroys without asking. | It needed a live GL HUD; #109 added no tests. | **39 tests, 10/10 mutants killed**: `test/inventory-input-controller` |
| 2 | `server/Carbon.Application/World/AuthoritativeWorld.TickAndAoi.cs:304` `ApplyDamageToPlayerLocked`, lethal branch :339-357 | Death must heal to full, teleport to the sanctuary, refill mana and stamina, reset needs, clear aggro and mark the character for saving. Otherwise players fight on at 0 HP, respawn in place, or lose state on logout. | No test kills a player. Death and respawn greps hit only `NeedsServiceTests.cs:102`. | tests added: `test/risky-path-coverage` |
| 3 | `server/Carbon.Application/World/Replication/OutboundTickBuffer.cs:212-222` `Dispatch` (slices `DeltaEntries` by offset and count), `AoiReplicator.cs:85` | An offset bug sends observer A's entity deltas to observer B. That is a correctness bug and an information leak. | `OutboundTickBufferTests` and `AoiReplicatorTests` use a single observer and never dispatch deltas. | tests added: `test/risky-path-coverage` |
| 4 | `server/Carbon.Application/World/Commerce/TradeService.cs:205` `FlushAsync`, catch and re-dirty at :233-244 | If a failed save is not retried, sold-out trader stock refills after a restart. That duplicates items through the economy. | No test uses a failing `ITraderStockRepository`. | tests added: `test/risky-path-coverage` |
| 5 | `desktop/Carbon.Desktop/Input/DesktopInputController.cs:56` `Poll` | A mis-mapped key fires the wrong action every frame (number keys, Delete, Enter, Ctrl, Shift-sprint). This mapping feeds rank 1. | No test file exists. `InputManager` is tested in graphics; this mapping is not. | tests added: `test/risky-path-coverage` |
| 6 | `server/Carbon.Application/World/Progression/CharacterProgressionService.cs:76` `Reconfigure` | The per-player loop (clamp level to the new `MaxLevel`, refund points, recompute stats) never runs with a player present. A lowered cap leaves players over budget. | `ConfigHotReloadDomainsTests.cs:67` only checks that the config object was swapped. | report only (next candidate) |
| 7 | `engine/Carbon.Economy/Vendor/VendorService.cs:146` `TrySellSlot` | It pays the base sell price regardless of `UpgradeLevel` and durability, so a +10 item sells like a +0. It is unclear whether that is intended; it needs a design decision before a test pins it. | No direct test. It is reached once through `TraderWorldTests.cs:81`. | report only: needs a design decision |
| 8 | `desktop/Carbon.Desktop/Scenes/TradeInteraction.cs:93` `HandleWindowInput` | Shift-click on a bag slot sells the whole stack without confirmation. Repair mode and close-when-out-of-reach are also untested. | It depends on the concrete `HudOverlayRenderer`, the same blocker that rank 1 had. | report only (reuse the `IInventoryInputHud` pattern) |

**Not applicable:** player-to-player trade does not exist yet. Only the `CurrencyReason.Trade` enum value (`engine/Carbon.Economy/Currency/CurrencyReason.cs:33`) exists.

**Well covered (no finding):**
- persistence: `CharacterPersistence` requeue, the SQLite round-trip, codecs v1–v3 and corrupt payloads;
- forge: `ForgeEnhancer` and the world tests;
- loot and pickup;
- the level-up-does-not-heal regression (`AuthoritativeWorldMultiplayerTests.cs:969`).

## Rules for new tests (from this audit)

1. A new or rewritten test must fail when the behaviour it names breaks. Record the mutation in the PR.
2. Expectations come from the requirement, not from the code. Use relations ("epic costs more per gold of value than common"), documented anchor points, or literal wire bytes. Do not re-run the production formula.
3. Do not add printouts or benchmarks to the unit-test run. A benchmark belongs in a benchmark project, and a balance table belongs in the balance doc.
4. Guards measure risk (complexity, allocations per tick, layer edges), not layout. An allowlist entry is pinned at the measured value and can only go down.

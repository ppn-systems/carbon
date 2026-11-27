# ADR-031: Player Death State, Respawn Request and Gold Drop on Death

- **Date**: 2026-11-27
- **Status**: Accepted
- **Amended by**: [ADR-038](ADR-038-retire-dead-wire-opcodes-and-fields.md) D7 (`PlayerDeath` carries only `Cause` and `KillerArchetypeId`)
- **Scope**: `contracts/Carbon.Contracts.Common`, `contracts/Carbon.Contracts.Items`, `engine/Carbon.Economy` (Config, Death,
  Currency), `engine/Carbon.Primitives` (Random), `server/Carbon.Abstractions`, `server/Carbon.Application` (World),
  `server/Carbon.Handlers`, `server/Carbon.Host`, `resources/config/economy.json`, `desktop/`, `resources/localization/`, `tests/`
- **Builds on**: [ADR-012](ADR-012-server-authoritative-wallet-persistence-and-sync.md) (wallet),
  [ADR-017](ADR-017-tick-queued-world-commands.md) (tick-queued commands),
  [ADR-019](ADR-019-ground-currency-pickups-and-kill-loot-protection.md) (ground coins and owner protection),
  [ADR-026](ADR-026-server-secret-gameplay-entropy.md) (gameplay entropy)

## Context

The owner asked for two changes. First, a player who dies should see a screen with a Respawn button instead of being
sent back automatically. Second, a death should drop a reasonable share of the player's gold, "not too little and not
too much", which other players can pick up like coins from monsters.

Before this change:
- The lethal branch of `AuthoritativeWorld.ApplyDamageToPlayerLocked` (`server/Carbon.Application/World/AuthoritativeWorld.TickAndAoi.cs`)
  refilled health in the same tick, queued a teleport to `ChunkWorld2D.SpawnWorldPosition` (the sanctuary), refilled
  mana, stamina and needs, and cleared nearby monster aggro. A player was never at 0 HP for longer than one call.
- Starvation (`AuthoritativeWorld.ProcessSurvivalEventsLocked`, `AuthoritativeWorld.Survival.cs`) went through the same branch.
- The client showed a "Slain" notice on `CombatHit.IsKilled` and nothing else (`MultiplayerSession.OnCombatHit`).
- A death cost nothing.
- `TryEnterWorld` clamped a saved `CurrentHealth` to at least 1, so a character saved at 0 HP would have come back alive
  where it fell.
- `HealthComponent.IsDead` (HP ≤ 0) already stops monster targeting in the engine (`MonsterBrainSystem`,
  `AiBehaviorSystem`, `ThreatTable`), survival needs (`NeedsSystem`) and eating (`EatActionSystem`).

## Decision

### Death state (server)

1. **D1 — Dead is HP 0 plus a flag.** On a lethal hit the player keeps 0 HP and `PlayerState.IsDead` is set
   (`AuthoritativeWorld.KillPlayerLocked`, `AuthoritativeWorld.Death.cs`). Velocity, queued movement, attack and pickup are
   cleared, any meal is cancelled, a resting player stands up, nearby monster aggro is cleared and every status effect is
   cleared (ADR-029). Monster hits, starvation and lethal status pulses all use this path. Settlement NPCs keep the old instant refill, since they have no client.
2. **D2 — A dead player cannot act.** `WorldCommandExecutor.Execute` answers every tick-queued command except `Respawn`
   with `Forbidden` while the player is dead. This covers building, harvesting, doors, resting, hotbar, crafting,
   inventory, enhancement, vendors, repair and point allocation. `TradeService` refuses dead players too.
   `EnqueueMoveInput` ignores input. `EnqueueAttackInput` (attacks, skills and consumables) and `EnqueuePickupLoot`
   return `Forbidden`. The tick skips movement, regeneration and auto-collect for dead players. Monster hits and
   projectiles skip a corpse. Stat recomputes and level growth leave a dead player at 0 HP (`CharacterProgressionService`,
   `VitalPools`).
3. **D3 — Replication.** Others see the corpse through the existing health delta (HP 0). There is no new
   entity state on the wire, so old clients still show a 0 HP player.
4. **D4 — No auto-respawn; respawn on request.** The player stays dead until the client sends `RespawnRequest`. The server
   queues it as `WorldCommandKind.Respawn` (ADR-017). `TryRespawn` is `Forbidden` unless the player is dead. When it is
   accepted, the player moves to the sanctuary with full health, mana and stamina, and needs are refilled with the
   existing rule (`NeedsService.ResetAfterDeath`: full, keeping at least half of max food). The reply is a `MutationResult`.
5. **D5 — Respawn point: the sanctuary.** The sanctuary (`ChunkWorld2D.SpawnWorldPosition`) is always generated, safe,
   and has the healer and blacksmith. A bed was rejected: beds have no owner, so "your bed" is undefined, and a bed
   anyone can respawn on would become a free teleport network. "Nearest village" was rejected: villages are materialized
   lazily per loaded chunk (ADR-025), so the answer would depend on which chunks happen to be loaded, and a
   village can sit next to a monster spawn. A bind point can be added later as its own decision.
6. **D6 — Disconnect while dead: respawn on next login.** A dead player who leaves is saved at 0 HP where they fell
   (`CharacterPersistence` already stores health and position). `TryEnterWorld` treats a saved `CurrentHealth ≤ 0` as
   "died before logout". It places the character at the sanctuary with full vitals and refilled needs, as a respawn
   would. Staying dead at the corpse was rejected. It needs a persisted dead flag (a migration) for no gameplay gain:
   the gold penalty was already taken at the moment of death, so logging out avoids nothing. New characters
   always start at full health (`AuthService.CreateCharacterAsync`), so 0 HP can only mean a death. No migration is needed,
   so id `0014` stays unused.

### Gold drop on death

7. **D7 — Formula.** The loss is

   ```
   loss = 0                                                    if level < minLevel or carried ≤ 0
   loss = min(carried, clamp(carried × dropPercent / 100, floorGoldPerLevel × level, capGoldPerLevel × level))
   ```

   It is computed in `DeathGoldPenalty.ComputeLoss` (`engine/Carbon.Economy/Death/DeathGoldPenalty.cs`). Diamonds are
   never dropped. Every number is in `economy.json → deathPenalty` (`DeathPenaltyRules`) and is validated at load. The
   file stays server-only (ADR-014 D10).

   | Key | Value | Why |
   | :--- | ---: | :--- |
   | `minLevel` | 10 | Newbie protection. Levels 1–9 keep only 38–312 net gold/hour and are meant to gear up (`docs/economy.md` §7). |
   | `dropPercent` | 10 | Most players carry a few hours of income. 10% hurts without wiping them out. |
   | `floorGoldPerLevel` | 2 | About 4 minutes of net income at every level (L10: 20 g of 330/h; L50: 100 g of 1,353/h; L100: 200 g of 2,864/h), so carrying no gold does not make death free. It never takes more than the player carries. |
   | `capGoldPerLevel` | 30 | About one hour of net income at every level (L10: 300 g vs 330/h, L20: 600 vs 635, L50: 1,500 vs 1,353, L75: 2,250 vs 2,340, L100: 3,000 vs 2,864). A rich player loses at most about an hour of play. |
   | `goldPerPile` | 250 | One pile per 250 g. |
   | `maxPiles` | 5 | Bounds the entities per death. |
   | `ownerProtectionSeconds` | 120 | Twice the kill-loot window (60 s), because the owner has to respawn and walk back from the sanctuary. |
   | `lootLifetimeSeconds` | 300 | Gold nobody claims despawns. That is a real gold sink, which the sink-share target welcomes. |

   Income figures are net gold/hour from `EconomyBalanceSimulatorTests` (`docs/economy.md` §7). Net income grows with
   about `level^0.6` faucets (§4) minus level-scaled sinks, so a per-level floor and cap keep the penalty at a roughly
   constant number of minutes of play. The 10% share is what applies between them, for a purse of 20×level to
   300×level gold (about 40 minutes to 10 hours of net income at level 50). Pinned values: level 10 with 1,000 g loses
   100; with 50 g it loses 20; with 15 g it loses 15; with 100,000 g it loses 300; level 9 loses nothing.
8. **D8 — PvE and PvP are the same.** There is no player-versus-player damage today. When it lands, it uses the same
   formula. A smaller PvP loss would invite "die to a friend to save gold". A larger one would reward griefing. The
   dead player's priority window (D9) already stops the killer from looting at once.
9. **D9 — Ground coins reuse ADR-019.** `EconomyService.DropDeathGold` debits the loss from the wallet with the new
   ledger reason `CurrencyReason.DeathDrop`. If the debit fails, nothing drops. It then spawns exactly that amount as
   `GroundLootService.SpawnCurrency` gold piles around the death spot, with a `LootClaim` owned by the dead character.
   During the window only the owner may pick them up, and the owner keeps the claim across a reconnect because the claim
   is keyed by character id. After the window anyone may, through the same manual pickup and auto-collect as monster
   coins. Picking a pile up credits it with `DeathDrop`, so the ledger records a transfer between players, not a faucet.
   The debit always equals the sum of the piles.
10. **D10 — Deterministic split.** `DeathGoldPenalty.SplitIntoPiles` makes `clamp(ceil(loss / goldPerPile), 1, maxPiles)` piles.
    Each pile gets 1 gold plus a weighted share (weights 50–150) of the rest, and the rounding remainder goes to the
    last pile. Weights and scatter offsets (10–30 px) come from
    `GameplayKey.CreateRandom(GameplayDomain.DeathDrop, tick, victimNetworkId)`. `GameplayDomain.DeathDrop` is
    `"GK_DEATH"`. The split is server-secret like every other roll (ADR-026).

### Wire (opcodes 0x4078–0x407F)

11. **D11 — New packets, append-only.**

    | Opcode | Packet | Direction | Members |
    | :--- | :--- | :--- | :--- |
    | `0x4078` | `PlayerDeath` | S2C, to the victim only | 0 `DeathCause Cause`, 1 `ushort KillerArchetypeId`, 2 `ulong KillerNetworkId`, 3 `long GoldDropped` |
    | `0x4079` | `RespawnRequest` | C2S | none; answered with `MutationResult` |

    `DeathCause : byte` (`Unknown = 0`, `Monster = 1`, `Starvation = 2`, append-only) is in `Carbon.Contracts.Common`.
    The server sends ids only. The client names a monster from its archetype id (`monster.<id>.name`) and the cause
    from its own `[death]` strings. `0x407A–0x407F` stay reserved for death and respawn.

### Client

12. **D12 — Death screen.** When `PlayerDeath` arrives (`DeathSession`, `desktop/Carbon.Desktop.Networking/Session/DeathSession.cs`),
    the desktop client covers the world with a dark, slightly red veil and opens the death screen (`DeathScreenWidget`).
    The screen shows the localized "You died" title, the killer's name (`monster.<id>.name`) or the cause, and a Respawn
    button. Enter also respawns. The gold drop is silent: the owner asked that the screen, toasts and HUD say nothing
    about it, so the client neither shows nor keeps `GoldDropped` (the server still drops the piles and the packet keeps
    the member). The first version showed the amount with the coin icon and a hint that others can take it. While dead, every other gameplay input is swallowed
    (`DeathScreenInput`) and open windows close. One press sends exactly one `RespawnRequest`. Further presses are
    ignored until the server answers: `Ok` closes the screen, and a refusal allows another try. All text comes from a
    new `[death]` section in `en.ini` and `vi.ini`. The obsolete "respawned at the sanctuary" toast (`hud.slain`) is gone.
13. **D13 — Corpse.** A player at 0 HP plays the new `downed` clip of `base_hero.cvg.json` (vector art, ADR-005). The
    local player is drawn downed while dead, and other players see a remote player at 0 HP lying on the ground. Each bone
    turns about the feet, because bones are not hierarchical. `CARBON_UI_PREVIEW=death` opens the screen with a sample
    death for `tools/screenshots` (scenarios `death_screen`, `death_screen_night`). It is visual only: nothing is sent.

## Alternatives Considered

- **A separate `Dead` ECS component instead of HP 0.** The engine already treats HP ≤ 0 as dead for targeting, AI, needs and
  eating. A second source of truth could drift from it.
- **Auto-respawn after a timer.** The owner rejected it explicitly.
- **Dropping items or equipment.** Not requested. It is a much larger economic change and would need its own ADR.
- **A flat percentage without floor or cap.** 10% of an empty purse is free, and 10% of a gold-capped wallet
  (2,000,000,000) is 200,000,000. The level-scaled bounds keep the cost at a few minutes to about an hour of play.
- **Persisting a dead state (migration 0014).** Rejected in D6.

## Consequences

- A death now costs between about 4 minutes and about one hour of net income from level 10 up, and nothing before.
  Unclaimed death gold despawns after 5 minutes and leaves the economy.
- The dead player's own client must send a respawn request. An old client without the death screen stays dead until
  it reconnects (D6).
- `PlayerDeathTests` (#118) asserted the old instant respawn. It now sends a `RespawnRequest` first and keeps its checks
  on vitals and the sanctuary (no respawn next to the killer).
- Status effects (ADR-029) are cleared in `KillPlayerLocked`, the single death path, and on a respawn at login. A
  lethal status pulse (bleeding) enters the same dead state. `StatusEffectWorldTests` now expects a dead player
  until the respawn request.
- Tests: `DeathGoldPenaltyTests` (pinned floor, cap and newbie values; split conservation and determinism; config
  validation), `DeathGoldDropTests` (debit equals piles, diamonds kept, owner priority, bystander pickup only after the
  window), `PlayerDeathStateTests` (stays dead, every action refused, no hits on the corpse, respawn only when dead,
  respawn vitals and position, death packet, gold conservation through a real monster kill, respawn on login),
  `WorldCommandExecutorTests.Drain_DeadPlayer_RefusesEveryCommandButRespawn`, and the updated starvation test.

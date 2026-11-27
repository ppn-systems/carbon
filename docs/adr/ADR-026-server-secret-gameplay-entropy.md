# ADR-026: Server-Secret Gameplay Entropy with Rotating Epoch Keys

- **Date**: 2026-11-27
- **Status**: Accepted
- **Scope**: `engine/Carbon.Primitives` (Random), `engine/Carbon.Economy` (Enhancement, Loot, Trading, Simulation),
  `engine/Carbon.Items` (Progression), `engine/Carbon.Survival`, `engine/Carbon.Creatures`, `server/Carbon.Application`
  (World, Entropy), `server/Carbon.Abstractions` / `server/Carbon.Infrastructure` (persistence), `server/Carbon.Host`
- **Amends**: [ADR-024](ADR-024-forge-enhancement-outcomes.md) D2 (forge roll stream) and
  [ADR-025](ADR-025-villages-from-real-structures-and-trader-npcs.md) trader stock seeding. Both used the world seed.

## Context

The owner's request: "The server must have its own seed for these calculations, otherwise the client can predict
them. Make it dynamic too if possible."

- The world seed is public. `MapHandlers.HandleWorldInfoAsync` sends it in `MapWorldInfo.Seed`
  (`contracts/Carbon.Contracts.Maps/S2C/MapWorldInfo.cs:23`), and `IdentityHandlers.SendEnterWorldAsync` sends it in
  `CharacterEnterWorld.WorldSeed` (`contracts/Carbon.Contracts.Identity/S2C/CharacterEnterWorld.cs:31`). Clients need it
  to generate the same terrain locally.
- The same seed also drove the gameplay rolls:
  - forge: `ForgeEnhancer.CreateRandom(worldSeed, tick, ownerId, nonce)` via `InventoryService.TryEnhance`;
  - loot: `LootRoller.CreateRandom(worldSeed, tick, sourceId)` via `EconomyService.TryRollLoot`;
  - trader stock: `TraderStockRoller.Roll(profile, worldSeed, …)` via `TraderBook(worldSeed)` in `TradeService`;
  - food poisoning: `EatActionSystem(worldSeed)` in `NeedsService`;
  - monster spawns and elites: `MonsterSpawnerSystem` keyed by `hash(seed, cell, attempt)`.
- Two rolls used no seed at all, so they were public functions of public data: crit and evasion
  (`CombatRoll.Roll(chance, nonce, salt)` with a per-character nonce), and the legacy catalog secondary and rare drops
  (`(tick * constant + monsterId) % 1000` in `AuthoritativeWorld.OnMonsterKilledLocked`).
- `ServerTick` is sent in `CharacterEnterWorld` and advances at a known rate. Network ids are in every spawn packet,
  and nonces are counters. So a client that knows the seed can compute every one of these outcomes in advance. For
  example, it can wait for a tick on which a +9 forge attempt succeeds, or preview a trader's next stock.

## Decision

1. **D1 — `GameplayKey`, a keyed PRF in the engine.** `engine/Carbon.Primitives/Random/GameplayKey.cs` is a 128-bit
   key with SipHash-2-4 over `domain ‖ a ‖ b ‖ c` (pinned to the reference test vector). `CreateRandom` seeds a
   `DeterministicRandom` from the PRF output, and `Sample` returns a value in `[0, 1)`. `GameplayDomain` assigns an
   8-byte tag per roll kind, so two kinds never share a stream. The key has no public constructor from integers, only
   `FromBytes`. `ToString` is redacted, and `WriteBytes` exists only for persistence. It is a pure, allocation-free
   struct, so the engine stays deterministic and AOT-safe without `System.Security.Cryptography`.
2. **D2 — Every gameplay roll takes the key, never a seed.**
   - `ForgeEnhancer.CreateRandom(in GameplayKey, tick, ownerId, nonce)`
   - `LootRoller.CreateRandom(in GameplayKey, tick, sourceId)`
   - `TraderStockRoller.Roll(profile, in GameplayKey, …)` and `TraderBook(GameplayKey)`
   - `CombatRoll.Roll(chance, in GameplayKey, domain, tick, actorId, nonce)`. Adding the tick and the actor stops a
     relog, which resets the nonce, from replaying the same crit sequence.
   - `FoodPoisoningRoll.Sample(in GameplayKey, …)`
   - `EatActionSystem` and `MonsterSpawnerSystem` take an `IGameplayEntropy` and roll on `KeyAt(tick)`.
   - The legacy secondary and rare drops roll on `KeyAt(tick).CreateRandom(MonsterDrop, tick, monsterId)`.
3. **D3 — `GameplayEntropy`, the server-only service** (`server/Carbon.Application/World/Entropy`):
   - Holds a 128-bit secret generated with `RandomNumberGenerator`. It is never derived from the world seed, never
     logged (`ToString` is redacted) and never placed in a packet.
   - `KeyAt(tick) = secret.Derive(Epoch, ⌊tick / epochTicks⌋, bootSalt)`. The epoch length defaults to 600 s of server
     time (`epochTicks = tickRate × 600`) and is configurable with `CARBON_GAMEPLAY_EPOCH_SECONDS`. Every roll in D2
     mixes this subkey with its own inputs. The stream therefore changes every epoch, and a leaked subkey stops being
     useful when its epoch ends.
   - The **boot salt** is 64 random bits per process. The tick counter restarts at zero on every boot, so without the
     salt a restarted server would replay the previous run's forge, loot and crit streams on the same ticks.
   - `TraderStockKey = secret.Derive(TraderStock, 0)` uses no salt and no tick epoch. Trader stock already rotates per
     restock epoch (ADR-025 D13, wall clock), so it must only change when the restock epoch does.
   - `AuthoritativeWorld` takes an optional `GameplayEntropy`. When none is given, it uses `CreateEphemeral` (a random
     secret for this process only), so nothing ever falls back to the seed.
4. **D4 — The secret is persisted per world.** Migration `0008_gameplay_secret` (SQLite and PostgreSQL) adds the
   single-row table `gameplay_secret(id = 1, secret, created_at_utc)`. `IGameplaySecretStore.GetOrCreateAsync` inserts a
   fresh CSPRNG secret only when no row exists, then reads the row back, so concurrent first boots agree on one value.
   At boot, `Program` calls `GameplayEntropy.LoadAsync(store, CARBON_GAMEPLAY_SECRET?, CARBON_GAMEPLAY_BOOT_SALT?, …)`.
   An injected secret (32 hex characters) is meant for tests and replays and bypasses the store. A malformed value fails
   the boot, and the error never echoes it.
5. **D5 — Terrain is unchanged.** `ProceduralWorldGenerator`, `Carbon.Maps` and everything the client mirrors keep
   using the public world seed. `GeneratorVersion` is unchanged.
6. **D6 — Guards.** `GameplayEntropyGuardTests` (architecture) enforce four rules:
   - every gameplay roll API and every method outside `Carbon.Maps` that returns a `DeterministicRandom` takes a
     `GameplayKey` or `IGameplayEntropy` and has no parameter named like a seed;
   - a Roslyn scan of `engine/` and `server/` (outside `Carbon.Maps`) fails when an identifier containing `seed` is
     passed to a roll call, to `GameplayKey.FromBytes`, to `Derive`, `Hash` or `KeyAt`, or to the constructors that take
     the entropy;
   - no contract member is named like a secret, entropy, subkey or boot salt, and no contract assembly references
     `Carbon.Primitives`;
   - `Carbon.Abstractions` and `Carbon.Handlers` expose no `GameplayKey` or `IGameplayEntropy`.

## Alternatives Considered

- **Stop sending the world seed.** Rejected. Clients need it to generate terrain, and streaming every chunk would be a
  wire protocol and bandwidth change for no gain once rolls stop depending on it.
- **`Random.Shared` / `RandomNumberGenerator` per roll.** Rejected. The outcome would be unpredictable but not
  replayable, which breaks the deterministic audits (ADR-012, ADR-024) and the tick-stepped tests.
- **HMAC-SHA256 per roll.** Rejected. It is heavier on the tick path and needs `System.Security.Cryptography` in the
  engine. SipHash-2-4 is a standard keyed PRF built for short inputs, and it is plain arithmetic.
- **Persist nothing; random secret per boot.** Rejected for traders. Every restart would reshuffle the current
  restock epoch's stock, while `trader_stock` would keep the sold counts of the old roll. Transient rolls get the same
  effect from the boot salt.
- **Derive trader stock from a separate persisted per-world value.** This is what D3/D4 do, but with one stored secret
  and domain-separated derivations instead of two stored values.
- **Wall-clock epochs for roll keys.** Rejected. Tick-based epochs keep `KeyAt` a pure function of the tick, so a
  replay with the same secret and salt reproduces it exactly.

## Consequences

- A client that knows the world seed, the tick, the ids and the nonces can no longer predict forge, loot, crit,
  evasion, food poisoning, spawn or trader outcomes (`ForgeEnhancerTests`, `ForgeEnhancementWorldTests`).
- Outcomes stay deterministic for a fixed secret and salt. Tests inject a fixed secret (`TestGameplayKeys`,
  `TestGameplayEntropy`). World tests that relied on what seed 1337 happened to spawn or roll now pin a test secret
  instead.
- Trade-off: the secret sits in the database in plain text. Anyone who can read the database can already change wallets
  and inventories, so this adds no new capability. Deployments that keep the database less trusted can inject the
  secret from a secret manager through `CARBON_GAMEPLAY_SECRET`.
- Trade-off: replaying a production incident needs the secret and the boot salt of that run. The salt is not logged.
  An operator can pin it with `CARBON_GAMEPLAY_BOOT_SALT` when a reproducible run is needed.
- Rotating `CARBON_GAMEPLAY_SECRET` or deleting the `gameplay_secret` row reshuffles the current trader stock once. Sold
  counts for that epoch then apply to the new roll. This is acceptable as an operator action.
- Each roll costs one or two SipHash evaluations (about 20 ns) and allocates nothing. The server tick allocation budget
  is unchanged (`ServerTickAllocationTests`).
- No wire protocol change: no packet gained, lost or renumbered a field.

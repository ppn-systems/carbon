# Carbon Codebase Audit — November 2026

| Field | Value |
| :--- | :--- |
| **Audited commit** | `6d6dfac` (master, PR #12). PRs #13 and #17, merged afterwards, are covered by the automated guards but were not manually re-audited; line numbers in files they touched may have shifted. |
| **Scope** | `contracts/`, `engine/`, `server/`, `desktop/`, `graphics/`, `tests/` (~79k lines C#, 31 production projects, 15 test projects) |
| **Method** | Manual read-through per layer against AGENTS.md, ADR-001…ADR-010, and the `csharp-style`, `carbon-engine-ecs`, `performance-aot`, `testing-style`, `carbon-handlers` skills; plus the reflection/metadata guards in `tests/architecture/Carbon.Architecture.Tests` |
| **Status** | Report only. No production code changed in this PR (economy, combat/AI, survival, building, config sync, and biome rendering are being changed in parallel). |

## How to read this

- **Severity:** **Critical** = exploitable or corrupts state in production; **High** = violates a project rule (AGENTS.md / ADR / skill) with real cost; **Medium** = design debt that will grow; **Low** = cleanup.
- Every finding has an ID (`SA-1`, `TS-2`, …), a `file:line`, and a concrete fix. Line numbers are at `6d6dfac`.
- **Guarded** means a test in `tests/architecture/Carbon.Architecture.Tests` now blocks *new* instances. The existing instances are listed in commented allowlists inside the test, so CI is green today. Remove an allowlist entry when you fix it; the `*_ShouldNotContainStaleEntries` tests fail if you forget.

## Executive summary

The *layer graph* is clean. Every `.csproj` respects AGENTS.md rule 3. No engine class is unsealed. No `async void`, LINQ in engine, `System.Random`, or `DateTime.Now` in simulation code. No SQL injection.

The real problems sit **inside** layers:

1. **Server authority is broken on the client and has holes on the server.** The desktop compiles in the engine's gameplay balance tables and uses them for costs, cooldowns, ranges, XP, and crafting decisions (SA-1…SA-7). Some copies already disagree with the server. On the server, prop harvest skips the cooldown and line-of-sight checks (SA-8), and anyone can demolish anyone's structures (SA-9).
2. **`AuthoritativeWorld` (server) and `MultiplayerSession` (client) are god classes.** They are 4.4k and 3.4k lines of partial-class sprawl that own a dozen domains each (SOLID-1, SOLID-2). This is the root cause of most DDD and thread-safety findings.
3. **Handlers mutate world state on I/O threads**, outside the tick queue that `carbon-handlers` mandates (TS-1). The desktop network thread mutates `ChunkWorld2D`/`WorldSimulation` while the render thread reads them (TS-2).
4. **Hot paths allocate.** Per-tick, per-observer arrays in delta broadcast (HP-1). The whole character is re-serialized on every hit (HP-2). Per-frame arrays and strings on the client (HP-5…HP-7).
5. **Gameplay data is hardcoded C#** (`StarterGameCatalog`, `WorldActorCatalog`, `SkillCatalog`), although an AOT-safe JSON loader exists and is used only by tests (DDD-1).
6. **Test pyramid is inverted on the server.** The 4.4k-line `AuthoritativeWorld` has 3 end-to-end tests. Handlers, Postgres, and the outbound sink have none (TEST-1…TEST-4).

## Top 10 findings

| # | ID | Severity | Title |
| :--- | :--- | :--- | :--- |
| 1 | SA-1 | Critical | Desktop compiles in and decides with server gameplay balance tables; no config-sync packet exists |
| 2 | SA-8 | Critical | `TryHarvestProp` skips the attack cooldown, line of sight and harvest range (resource/XP farming at ~240/s) |
| 3 | TS-2 | Critical | Desktop network thread mutates `ChunkWorld2D`/`WorldSimulation` while render reads them unlocked |
| 4 | SA-9 | High | Any player can demolish any structure and keep 60% salvage (no owner) |
| 5 | TS-1 | High | Handlers mutate world state synchronously on I/O threads (craft/move/harvest/build), bypassing the tick queue |
| 6 | TS-3 | High | Stale load during an in-flight save can roll back or duplicate inventory |
| 7 | SOLID-1 | High | `AuthoritativeWorld` god class: 5 partials, 4,362 lines, 54 fields, ~17 responsibilities |
| 8 | HP-1 | High | `WorldOutboundSink.SendEntityDeltaBatch` allocates 7 arrays + a `Task` per observer per tick |
| 9 | HP-2 | High | Every state change re-serializes the full inventory + progression blob (ADR-007 drift) |
| 10 | SEC-1 | High | Unauthenticated login: no per-IP/account throttling, uncapped password length, PBKDF2 on the I/O thread |

---

## 1. Layer boundaries (AGENTS.md rule 3)

**Result: compliant.** Every project reference and compiled assembly reference respects the rule. Guarded by `LayerDependencyTests` (project graph + assembly metadata + restricted packages: Silk.NET/StbImageSharp only in graphics/desktop; Npgsql/Sqlite/Nalix.Hosting/Nalix.Network only in server).

| ID | Sev | Finding | Fix |
| :--- | :--- | :--- | :--- |
| LB-1 | Medium | Rule 3 lets desktop reference *all* of engine, so the client links the balance catalogs (`Carbon.Items`) and server-only systems. `desktop/Carbon.Desktop.Networking/Carbon.Desktop.Networking.csproj:19-20` references `Carbon.AI` and `Carbon.Combat`, but desktop has no `using` of either. | Remove the two dead references now. In an ADR, split `Carbon.Items` into `Carbon.Items` (ids, slot structs, codecs: shared) and a server-only balance assembly. Then extend `LayerDependencyTests.s_allowedLayerReferences` to forbid desktop → balance. |
| LB-2 | Low | `tests/desktop/Carbon.Desktop.Tests/Carbon.Desktop.Tests.csproj` references server Application/Infrastructure/Host for one end-to-end test. | Move the end-to-end test into `tests/integration/` so desktop unit tests stay free of server code. |
| LB-3 | Low | `server/Carbon.Abstractions/World/IWorldMapService.cs:29` returns the pooled wire DTO `MapChunkDelta`. `ISessionTransportRegistry` takes Nalix `IConnection`. Transport and wire types leak into the application layer. | Return a domain `ChunkDeltaView` and map it in Handlers. Move `ISessionTransportRegistry` to Handlers/Host. |

## 2. Server authority (client must not own gameplay decisions or tables)

The server must send gameplay config; the client may only predict and display. Guarded by `ServerAuthorityTests`, which blocks new desktop uses of engine balance types and new gameplay-named numeric constants. The 36 existing type usages and 2 constants are allowlisted.

| ID | Sev | Finding | Fix |
| :--- | :--- | :--- | :--- |
| SA-1 | **Critical** | Desktop reads `StarterGameCatalog`, `SkillCatalog`, `WorldActorCatalog`, `CharacterProgressionPolicy`, `ItemEnhancementPolicy`, `ToolUsagePolicy`, `WorldProgression` in 21 files (~70 call sites; see the allowlist in `ServerAuthorityTests`). No packet in `contracts/` carries item/recipe/skill/actor config, so a server balance change silently desyncs every client. | ADR + append-only S2C `GameConfigSync` packet (items, recipes, skills, NPC capabilities, progression curve, ranges, movement), sent at `CharacterEnterWorld`, behind a client `IGameConfigProvider`. Migrate call sites and delete allowlist entries one by one. (Coordinate with the config-sync work in flight.) |
| SA-2 | High | The client resolves skill mana/stamina costs and cooldowns from `SkillCatalog` and deducts them locally: `desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.Actions.cs:151-193`. | Send the cast request. Treat cost and cooldown as prediction only, from server-sent config; the authority is `PlayerStatsSync`. |
| SA-3 | High | Movement and regen are copied from the server: `MultiplayerSession.cs:37-38` (`WalkSpeed=250f`, `SprintSpeed=450f`), `:542,546,551` (stamina/mana rates). They duplicate `server/Carbon.Application/World/AuthoritativeWorld.cs:39-40` and `AuthoritativeWorld.TickAndAoi.cs:53,68`. Terrain speed multipliers are missing, so prediction drifts. | Put them in `CharacterEnterWorld`/config sync, or in one shared engine `MovementSpec` used by both sides. |
| SA-4 | High | The client computes the XP curve and unspent points: `MultiplayerSession.Packets.cs:416-417,835`, `MultiplayerSession.Auth.cs:290-291`. | Append `UnspentStatPoints`/`UnspentSkillPoints` (and the level threshold) to `CharacterEnterWorld`/`PlayerStatsSync`. |
| SA-5 | High | The client decides interaction ranges, and they disagree with the server and with each other: loot `84f` (`MultiplayerSession.Actions.cs:554`) vs server `36f` (`AuthoritativeWorld.InventoryAndLoot.cs:647`); tool reach `56f` (`Actions.cs:131`) vs `52f` (`desktop/Carbon.Desktop.Rendering/World/WorldSceneRenderer.cs:239`); forge `175f` in 5 places (`MultiplayerSession.cs:285`, `IWorldReadModel.cs:139`, `WorldEntityOverlayRenderer.cs:90,206,364`). | Read ranges from config sync in one place. Fix the 52/56 mismatch at once. |
| SA-6 | High | The forge preview computes cost, success chance and stat multiplier on the client: `desktop/Carbon.Desktop.Rendering/Hud/ForgeAnvilModalWidget.cs:158,202,284-287,324`. The crafting UI gates on the client recipe table: `CraftingModalWidget.cs:110-374`, `MultiplayerSession.Actions.cs:649-676`. | Server-sent enhancement table or per-item quote packet. Client checks stay cosmetic; the server's `OpStatus` always decides. |
| SA-7 | High | Client animation durations set the effective auto-attack rate: `MultiplayerSession.Actions.cs:939-957` (`ResolveActionDuration`), consumed by `desktop/Carbon.Desktop/Scenes/WorldGameplayScene.cs:430`. | Take swing timing from a server-sent attack-speed field; client durations become visual only. |
| SA-8 | **Critical** | `TryHarvestProp` (`server/Carbon.Application/World/AuthoritativeWorld.WorldPropsAndBuilding.cs:23-60`, called directly from `server/Carbon.Handlers/Maps/MapHandlers.cs:118`) checks only the 3.5-tile interact range. The tick-path harvest is gated by the attack cooldown (`AuthoritativeWorld.CombatAndSkills.cs:54-59`), a 52px range, and line of sight. With the global 240 packets/s guard (`server/Carbon.Host/Startup.cs:83`), a client can harvest ~240×/s, through walls, from twice the range. | Route `MapPropHarvest` into the tick-queued attack/harvest path, or call the cooldown, `IsWithinHarvestRangeLocked` and `HasLineOfSightLocked` checks first. Delete the duplicate path (see DUP-1). |
| SA-9 | High | `TryDemolishStructure` (`AuthoritativeWorld.WorldPropsAndBuilding.cs:243-290`) checks only range. There is no owner (`StructureTile` has no owner field), no cost, and no LoS check. It also refunds 60% of the materials (`:565-596`). That allows instant griefing and theft. | Store the placer's character id on the structure. Allow demolish only by the owner; others must damage it. Add a LoS check. (Coordinate with the building work in flight.) |
| SA-10 | Medium | Chunk delta requests accept any coordinates (`server/Carbon.Handlers/Maps/MapHandlers.cs:59-70`), which leaks every player base location and costs work under `_gate`. | Reject chunks outside the requester's streaming radius; rate-limit per connection. |
| SA-11 | Medium | Placement validity is duplicated in the renderer (`WorldSceneRenderer.cs:909-918`), and the renderer builds its own `ProceduralWorldGenerator` (`:107`). | Expose `CanPlaceAt(tile)` and `Landmarks` from the session or a domain service; the renderer only draws. |
| SA-12 | Medium | UI formula duplication: `CharacterSheetModalWidget.cs:245` (`pwrPts * 3.5f`) mirrors `engine/Carbon.Items/Catalog/CharacterProgressionPolicy.cs:115`. Character select hardcodes starter gear (`desktop/Carbon.Desktop/Scenes/CharacterSelectScene.cs:45,363-372`). Q/R/V are hardwired to skill ids (`desktop/Carbon.Desktop/Input/DesktopInputController.cs:91-93`). | Display server-sent derived stats. Add equipped item ids to `CharacterRosterEntry`. Map keys to skill-bar slots. |
| SA-13 | Low | Crit and loot rolls are predictable: they hash a per-session nonce starting at 0, or `tick + netId` (`AuthoritativeWorld.CombatAndSkills.cs:348-361,442-451`). | Seed from a server-secret RNG per session behind an `IRandomSource`. Keep deterministic seeds only for tests. |

## 3. ECS (components = unmanaged data, systems = stateless logic)

Guarded by `EngineDesignTests`. Components (`*.Components` namespaces or `*Component` types in engine) must be unmanaged structs with no behavior methods. Today all of them are unmanaged. One behavior method is allowlisted.

| ID | Sev | Finding | Fix |
| :--- | :--- | :--- | :--- |
| ECS-1 | High | The engine combat pipeline is unused: `engine/Carbon.Combat/Projectiles/ProjectileSystem.cs:77-87` and `Statuses/StatusEffectSystem.cs:91-99` are never registered (the server registers only AI + loot expiry: `AuthoritativeWorld.cs:152-153`). The server re-implements damage in `AuthoritativeWorld.CombatAndSkills.cs:392-396`, so defense, invulnerability and kill rules exist in several diverging copies. | One engine `DamageService.Apply(world, attacker, target, amount)` used by the server and both systems, or delete the unused systems. (Coordinate with the combat/AI work in flight.) |
| ECS-2 | High | Crowd control has no effect: `StatusEffectSystem.cs:50-56` writes `CombatStateComponent.MoveSpeedScale`, but nothing reads `MoveSpeedScale`/`CanMove`/`CanAct`/`CanCast` (`engine/Carbon.Combat/Components/CombatStateComponent.cs:14-34`). `AiBehaviorSystem` sets velocity unconditionally (`engine/Carbon.AI/Systems/AiBehaviorSystem.cs:677-690`). | Apply the CC flags in `MovementSystem` (or a Movement-phase system after AI). Move `CombatStateComponent` where AI can see it. |
| ECS-3 | Medium | Per-entity heap state goes through `EntityAttachmentStore<T>`: `ThreatTable` (`engine/Carbon.AI/Threat/ThreatTable.cs:11`), `PathWaypointBuffer` (`engine/Carbon.AI/Navigation/PathWaypointBuffer.cs:12`), `StatSheet` with `List<StatModifier>` (`engine/Carbon.Combat/Stats/StatSheet.cs:12,18`), and `StatusEffectBuffer`. This contradicts the skill rule against heap collections in components. | Use fixed-capacity unmanaged structs (`[InlineArray]`) in `ComponentPool<T>`, or write an ADR that accepts attachments as a documented exception. |
| ECS-4 | Medium | `HealthComponent` (`engine/Carbon.Simulation/ECS/Components/HealthComponent.cs:13`) enforces no `0 ≤ Current ≤ Max`. Every caller clamps by hand (`ProjectileSystem.cs:79-80`, `StatusEffectSystem.cs:91-106`, `AiBehaviorSystem.cs:312,333-334`, server). | Add pure static helpers (`HealthMath.Damage/Heal`) that clamp, and use only those. |
| ECS-5 | Low | `Collider2D.GetWorldAabb` is a behavior method on a component (allowlisted in `EngineDesignTests`). | Move it to a static `Collider2DMath` helper and delete the allowlist entry. |
| ECS-6 | Low | `AiBrainComponent` (`engine/Carbon.AI/Components/AiBrainComponent.cs:27-43`) mixes 16 fields of static tuning with runtime state, and is copied with `with` several times per tick. | Split into an `AiProfileRef` (config) and a small `AiRuntimeState` mutated via `ref`. |
| ECS-7 | Low | Systems hold scratch buffers (`ProjectileSystem._toDespawn` `:25`, `LootExpirationSystem._expired` `engine/Carbon.Items/Systems/LootExpirationSystem.cs:19`, `AiBehaviorSystem._pathfinder` `:35`), so they are not re-entrant. | Document single-threaded system execution, or pass a scratch context into `Update`. |
| ECS-8 | Medium | `EntityManager.DestroyEntity` is public (`engine/Carbon.Simulation/ECS/EntityManager.cs:129`) and reachable through `WorldSimulation.Entities`. It bypasses the spatial grid and the network-id map (`WorldSimulation.cs:593,596`). | Make it internal and route all removals through `WorldSimulation.DespawnEntity`, or add a destroy hook. |

## 4. SOLID

| ID | Sev | Finding | Fix |
| :--- | :--- | :--- | :--- |
| SOLID-1 | High | **`AuthoritativeWorld` god class.** `server/Carbon.Application/World/AuthoritativeWorld*.cs`: 5 partials, 4,362 lines, 54 fields, 16 consts, ~80 methods, 12 pairs of double-buffered outbound lists (`AuthoritativeWorld.cs:81-105`). It owns sessions, input, movement, regen, melee/ranged/skills, projectiles, AI attacks, death/respawn, population, NPC services, loot, inventory, crafting, enhancement, progression, harvesting, building, chunk streaming, AoI, outbound dispatch, persistence and net-id allocation. Partial files hide the size; they do not split responsibility. | Keep a thin `WorldTickCoordinator` (gate, tick, `WorldContext`). Extract `PlayerSessionRegistry`, `CombatService`, `ActorPopulationService`, `LootService`, `InventoryService`, `ProgressionService`, `HarvestAndBuildingService`, `ReplicationService` (one `OutboundQueue` struct instead of 24 lists) and `PersistenceCoordinator`, each behind the existing `IWorld*` port it implements. Do this after the parallel gameplay PRs land, one service per PR. |
| SOLID-2 | High | **`MultiplayerSession` god class (client).** `desktop/Carbon.Desktop.Networking/Session/MultiplayerSession*.cs`: 4 partials, 3,440 lines covering transport, auth, roster, prediction, inventory rules, crafting, HUD notifications and VFX state. | Extract `ConnectionManager`, `LocalPlayerState`, `RemoteEntityStore`, `InventoryClientModel` and `FeedbackEventQueue`; keep `MultiplayerSession` as a facade. |
| SOLID-3 | Medium | `WorldSimulation` (`engine/Carbon.Simulation/World/WorldSimulation.cs:29`, 717 lines) owns the clock, command queue, event bus, raycasts, overlap queries, net-id map, interpolation and dirty tracking. Movement and collision are hard-wired (`:200,207`), not registered systems (OCP). | Extract `WorldQueries`, `NetworkIdentityRegistry`, `SimulationEventBus` and `CommandProcessor`. Make Movement/Collision default-registered `ISimulationSystem`s. |
| SOLID-4 | Medium | `AiBehaviorSystem.cs:64-359` is one 300-line state switch full of magic tuning (`1.18f` `:205`, `64f` `:231,278`, `0.38f/0.65f` `:282,288`, `26f` `:459`), and it also does leash regen (`:307-337`). | One `IAiStateHandler` per state, tuning in a data-driven `AiProfile`, regen in its own system. |
| SOLID-5 | Medium | ISP: `IWorldOutboundSink` is a 10-method fat interface mixing unicast, multicast and every domain (`server/Carbon.Abstractions/World/IWorldOutboundSink.cs`). The client `IWorldReadModel` (`desktop/Carbon.Desktop.Abstractions/World/IWorldReadModel.cs`) has 58 members, exposes mutable `ChunkWorld2D`/`WorldSimulation` (`:26,29`), and has `Drain*` mutators (`:142-151`). `LoginScene` takes the full `IWorldSession` (`desktop/Carbon.Desktop/Scenes/LoginScene.cs:35`). | Split into per-domain ports (replication/combat/inventory/map). On the client: `IWorldSnapshot` (read-only), `IFeedbackEventSource` (drains), `IChunkView`. Inject the narrowest interface. |
| SOLID-6 | Medium | DIP: `AuthoritativeWorld` constructs `ProceduralWorldGenerator`, `ChunkWorld2D`, `WorldSimulation`, `AiBehaviorSystem` and `SpatialAoiTracker` itself (`AuthoritativeWorld.cs:146-154`), and reads static catalogs throughout. None of them can be substituted in tests. | Inject a `WorldContext`/factory and catalog interfaces from the composition root (`server/Carbon.Host/Program.cs`). |
| SOLID-7 | Medium | Handlers use Nalix `[Inject]` static service-locator fields: `server/Carbon.Handlers/Entities/EntityHandlers.cs:23-24`, `Identity/IdentityHandlers.cs:31-33`, `Items/ItemHandlers.cs:26-27`, `Maps/MapHandlers.cs:26-27` (allowlisted in `CodeHygieneTests`). | If Nalix supports instance handlers, switch to constructor injection; otherwise wrap the statics in one `HandlerServices` holder and document it in an ADR. |
| SOLID-8 | Low | Large client renderers: `ChunkTerrainRenderer.cs` (1,282 lines), `PointOfInterestRenderer.cs` (1,147), `WorldSceneRenderer.cs` (1,036), `CharacterSelectScene.cs` (922, 37 fields). | Split by concern (terrain bake / water FX / roads; one renderer per POI type; `CharacterCreatorPanel` + `RosterPanel`). |

## 5. DDD (rich domain, invariants, aggregates)

| ID | Sev | Finding | Fix |
| :--- | :--- | :--- | :--- |
| DDD-1 | High | Gameplay tables are hardcoded C#: `engine/Carbon.Items/Catalog/StarterGameCatalog.cs:135` (39 items, with UI strings such as `"Tier 1 Axe \| Chop Trees (55 dmg)"`) and `:281` (34 recipes); `WorldActorCatalog.cs:137,148` (monsters/NPCs/drop rates); `SkillCatalog.cs:121`. The AOT-safe `engine/Carbon.Data/Serialization/JsonDefinitionLoader.cs:15` is used only by tests. | Move the tables to `resources/` JSON loaded through `JsonDefinitionLoader` (source-generated `JsonTypeInfo`). Keep only id constants in C#; display strings go to localization. `ResourceTests.AllResourceJsonFiles_ShouldParse` already guards the files. |
| DDD-2 | Medium | Anemic player aggregate: `ActivePlayerState` (`AuthoritativeWorld.cs:1093-1126`) has only public setters. Quantity, durability and HP/mana clamps are hand-written at each call site (`CombatAndSkills.cs:243-246`, `InventoryAndLoot.cs:457-460`, `WorldPropsAndBuilding.cs:400-403`, `TickAndAoi.cs:239-241`). HP lives in ECS, mana/stamina in the state bag. | A `PlayerCharacter` aggregate with behavior (`ConsumeOne(slot)`, `Degrade(slot, loss)`, `SpendMana`, `ApplyDamage`, `Heal`) that enforces invariants; make the state private. |
| DDD-3 | Medium | Durable items can stack: `engine/Carbon.Items/Containers/ItemSlot.cs:33` `IsStackable` ignores durability, and `InventoryContainer.TryAdd(..., maxStackSize = 99)` (`InventoryContainer.cs:94`) / `RecipeMatcher.TryCraft(..., 99)` (`engine/Carbon.Items/Crafting/RecipeMatcher.cs:49`) will merge two swords. `SetSlot` (`:59-64`) never checks limits. | Treat `Durability > 0` as non-stackable. Require the registry overload instead of the magic `99`. |
| DDD-4 | Medium | Max durability is written in three places: `StarterGameCatalog.cs:138` (`120f`) vs `:339`; `StonePickaxe 160f` at `:142,286,344`. | Add an `ItemArchetype.CreateFresh(quantity)` factory that takes durability from the archetype. |
| DDD-5 | Medium | Dropped items are re-collected by their dropper ~0.4 s later: they spawn 28.6px away (`AuthoritativeWorld.InventoryAndLoot.cs:466`) and auto-collect radius is 32px (`:631-647`). The ADR-007 owner grace is not implemented; `LootDropComponent.OwnerEntityId` is always 0 (`:576,601,701`). | Spawn beyond the collect radius or tag the dropper with an exclusion window. Set and enforce the owner (`Entity`, not a raw slot id). |
| DDD-6 | Medium | Contracts contain encoding logic that loses data silently: `contracts/Carbon.Contracts.Entities/AbilityInputEncoding.cs:28-56` (`% 10`, `% 20` alias archetypes ≥ 10). `EncodeSkill` accepts ids 1-109, but `CharacterSkillMask.cs:29-41` covers only 1-64. | Move the helpers to engine, use `TryEncode*` that fails on overflow, and align the skill-id ranges. |
| DDD-7 | Low | Server balance defaults sit in packet initializers: `contracts/Carbon.Contracts.Identity/S2C/CharacterEnterWorld.cs:71-107`, `contracts/Carbon.Contracts.Entities/S2C/PlayerStatsSync.cs:24-32` (HP 100, Mana 80, …). | Default to 0; the server sets every field. |
| DDD-8 | Low | Raw `byte` where an enum exists: `CharacterCreate.StartingClass`, `CharacterEnterWorld.ClassId`, `CharacterSummary.ClassId` (`contracts/Carbon.Contracts.Identity/...`). | Use `HeroArchetypeClass` (same wire width). |

## 6. Thread safety

Guarded by `CodeHygieneTests`: no `async void` anywhere (none today), and no static mutable state in engine/server. 21 fields are allowlisted with reasons (type-id counters, `[ThreadStatic]` scratch, `[Inject]` statics, auth map, river caches).

| ID | Sev | Finding | Fix |
| :--- | :--- | :--- | :--- |
| TS-1 | High | Handlers mutate world state on I/O threads. Craft, move, enhance, drop, hotbar select, harvest, place, demolish, door toggle and point allocation run synchronously under `_gate` on network threads (`AuthoritativeWorld.InventoryAndLoot.cs:49,119,201,305,428`; `WorldPropsAndBuilding.cs:24,143,243,293`; `AuthoritativeWorld.cs:421`). They then send packets outside the tick dispatch order. This violates `carbon-handlers` ("enqueue to the World Tick queue"). | Turn them into command structs on a `ConcurrentQueue`, drained at the start of `StepFixedTick`, with replies through the same outbound buffers. |
| TS-2 | **Critical** | Desktop: the network thread mutates `ChunkWorld2D`/`WorldSimulation` under `_worldGate` (`MultiplayerSession.Packets.cs:716,1157,1243`; `_simulation = new …` on reconnect `:449`). The render thread reads the raw objects with no lock (`MultiplayerSession.cs:138,142`; `WorldSceneRenderer.cs:185,486-497`; `MultiplayerSession.Actions.cs:771,867`). | Queue network-side mutations into an inbox applied in `Update()` on the main thread, or publish an immutable per-frame snapshot. |
| TS-3 | High | `FlushDirtyPersistenceAsync` clears `_dirtyCharacters` under the lock, then awaits `SaveBatchAsync` outside it (`AuthoritativeWorld.TickAndAoi.cs:350-372`). A re-select during the await loads the stale DB row (`server/Carbon.Handlers/Identity/IdentityHandlers.cs:189-204`), which rolls back or duplicates items. | Keep an `_inFlightCharacters` map that `TryGetPendingCharacterSnapshot` reads, and clear entries only after commit. Optionally add an `updated_at` optimistic-concurrency check. |
| TS-4 | High | Pooled `IPacketContext` is used after `await` (`IdentityHandlers.cs:56,59,83,86,124,134,147,172,185,195,205`; `ItemHandlers.cs:52,60`; `MapHandlers.cs:92,106`). If the context is recycled, `Connection.Level = USER` can land on the wrong connection. | Copy `context.Connection`/`context.Sender` into locals before the first `await`. |
| TS-5 | High | Persistence I/O is awaited inside the tick loop, and a tick exception stops the world silently while the network keeps accepting players (`server/Carbon.Host/WorldTickHostedRunner.cs:41-51`, `Program.cs:120`). | Run the flush as a separate, non-overlapping loop. Wrap `StepFixedTick` in try/catch with logging and a metric; fail the host if the tick task faults. |
| TS-6 | High | Engine chunk pool recycling races lock-free readers: `engine/Carbon.Maps/Streaming/ChunkStreamer.cs:53-54,73-74,125-127`; `ChunkWorld2D.cs:204-205` (and `:243,282,427,441`). `TryDamagePropAt` takes the chunk outside `_gate` and edits it inside, possibly after it was recycled for other coordinates. | Look the chunk up again inside the lock before editing. Retire pooled chunks with a generation stamp/epoch, or stop pooling. |
| TS-7 | Medium | `RiverNetwork` uses process-wide static caches (`engine/Carbon.Maps/Hydrology/RiverNetwork.cs:66-69`). `CacheKey` omits the `_relief` instance that the `Lazy` captures. `Task.Run` at `:127` is fire-and-forget, and faulted `Lazy` values are cached forever. | Make the cache an instance owned by the generator. Observe prewarm failures; don't cache faults. |
| TS-8 | Medium | `ConnectionAuthRegistry` is a static process-wide auth map (`server/Carbon.Handlers/Session/ConnectionAuthRegistry.cs:14`), duplicated with `Connection.Level`. | Injected singleton, or keep the account id on connection attributes. |
| TS-9 | Medium | Chunk generation runs under the global lock on I/O threads (`AuthoritativeWorld.cs:234-235`) and in the tick every 10 ticks (`TickAndAoi.cs:136-140`). | Generate off-lock (prefetch queue), apply at tick start, and throttle per tick. |
| TS-10 | Medium | Client `_localMana`/`_localStamina` read-modify-write outside the lock (`MultiplayerSession.Actions.cs:188-189`) races with the network thread (`MultiplayerSession.Packets.cs:844-846`). `_tcpSession`/`_udpSession` (`MultiplayerSession.cs:65-66`) are reassigned on reconnect threads without `volatile`. | Lock, or drop the optimistic deduction (SA-2). Use `Volatile.Write` for session swaps. |
| TS-11 | Low | Desktop `static readonly List<>` scratch lists mutated in instance methods: `desktop/Carbon.Desktop.Rendering/World/ChunkTerrainRenderer.cs:47,57,364`. | Make them instance fields. (The static-state guard covers engine/server only; extend it to desktop after this fix.) |
| TS-12 | Low | `[ThreadStatic]` fields use the `s_` prefix instead of `t_` (`RoadStage.cs:17-18`, `ProceduralWorldGenerator.cs:37-38`, `RoomDetector.cs:154-155`). | Rename to `t_` and update the allowlist. |

## 7. Hot-path allocations (AGENTS.md rule 4, `performance-aot`)

| ID | Sev | Finding | Fix |
| :--- | :--- | :--- | :--- |
| HP-1 | High | `server/Carbon.Host/WorldOutboundSink.cs:91-97` allocates `ulong[]`, `byte[]` and five `float[]` per observer per tick (30 Hz). Every send also allocates a fire-and-forget `Task` (`_ = Dispatch…Async`, lines 53, 64, 113, 130, 153, 165, 180, 184, 194, 215, 225, 243), with no per-connection ordering or backpressure. | Rent the columns from `ArrayPool` and return them on send completion. Use a per-connection send channel (`ValueTask`), which also fixes ordering. |
| HP-2 | High | `MarkCharacterDirtyLocked` → `BuildUpdatedRecordLocked` re-serializes inventory and progression and allocates a new record on every hit, durability tick and cast (`AuthoritativeWorld.cs:770-821`; callers `CombatAndSkills.cs:345,595,630`, `TickAndAoi.cs:243`). ADR-007 says serialization is deferred to flush/logout. | Only set dirty flags; build records in `FlushDirtyPersistenceAsync`/`RemovePlayerLocked`. |
| HP-3 | High | `BuildPlayerStatsSnapshotLocked` allocates three arrays and a fresh progression blob per player every 10 ticks, changed or not (`AuthoritativeWorld.cs:989-1006`). | Send only when `StatsDirty`, cache the blob behind a dirty flag, and use pooled cooldown buffers. |
| HP-4 | Medium | O(players × loot) and O(events × players) scans every tick: `InventoryAndLoot.cs:621-649`; `TickAndAoi.cs:756-773` (`CollectNearbyConnectionsAroundWorldLocked`); `CombatAndSkills.cs:636-660`. | Query the spatial grid (`LootLayer`, `PlayerLayer`), and reuse `SpatialAoiTracker` visible sets for recipients. |
| HP-5 | High | The client rebuilds `RemoteEntityView[]` every frame, several times: `_remoteViewsDirty = true` is set unconditionally at `MultiplayerSession.cs:493`, and the getter at `:338-378` allocates. | Mark dirty only on change, and double-buffer pooled arrays exposed as `ReadOnlySpan`. |
| HP-6 | Medium | Per-frame strings and arrays: `CharacterSheetModalWidget.cs:241-252`; the window title at `WorldGameplayScene.cs:819,854`; overlays `WorldEntityOverlayRenderer.cs:95-381`; `CarbonUiSkin.cs:220` (`TruncateToWidth` allocates on every loop iteration, O(n²)). | Cache labels per entity/item and invalidate on change. Throttle the title. Binary-search the truncation length. |
| HP-7 | Medium | A closure + `Task` per movement tick: `MultiplayerSession.Actions.cs:883-896` (also `:118,227,815`), passing `CancellationToken.None` (`Packets.cs:617,642`). | A non-capturing typed overload returning `ValueTask`; forward the session token. |
| HP-8 | Medium | Tick code can fall back to synchronous world generation: `ChunkWorld2D.GetMoveSpeedMultiplier`/`IsRoadAtWorld` (`engine/Carbon.Maps/Streaming/ChunkWorld2D.cs:486-532`) → `PeekTileAtWorld` → `_generator.SampleTile` (`:385`), called from `WorldSimulation.cs:667` and `AiBehaviorSystem.cs:158,339,540`. | Return defaults for non-resident chunks on the tick path. |
| HP-9 | Medium | A* has no per-tick budget; repaths synchronize after state changes (`AiBehaviorSystem.cs:376-380,433-437`; `engine/Carbon.Maps/Navigation/GridPathfinder.cs:50`). | Add a global expansion budget/repath queue and stagger cooldowns by entity id. |
| HP-10 | Medium | `StatusEffectSystem` scans all entities per tick (`StatusEffectSystem.cs:36-45`). | Iterate a dense owner list. |
| HP-11 | Medium | Spatial queries truncate silently when the buffer fills (`engine/Carbon.Simulation/Spatial/SpatialHashGrid2D.cs:292-299`; 16-512-entry scratch buffers in `WorldSimulation.cs:32`, `AiBehaviorSystem.cs:29-30`, `HitboxQuery.cs:16`). The nearest target can be missed in crowds. | Report overflow and fall back to a pooled larger buffer. |
| HP-12 | Low | `ChunkStreamer.cs:148-155` allocates and sorts a snapshot array on every chunk create. `RoomDetector.DetectRoom` (`engine/Carbon.Maps/Building/RoomDetector.cs:32`) allocates a Queue/HashSet/List per call despite claiming zero allocations (`:90`). | Rebuild once per `Stream()` batch. Reuse `EnclosureScratch`. |

## 8. Security

| ID | Sev | Finding | Fix |
| :--- | :--- | :--- | :--- |
| SEC-1 | High | Login (`PermissionLevel.NONE`) runs 100k-iteration PBKDF2 synchronously, with no per-IP/account throttling and no password length cap in `LoginAsync` (`server/Carbon.Handlers/Identity/IdentityHandlers.cs:63-87`, `server/Carbon.Application/Identity/AuthService.cs:92-111`). That enables CPU exhaustion and online brute force. | Per-IP and per-account failed-attempt limits with backoff, a length cap, and hashing on a bounded worker. |
| SEC-2 | Medium | Username enumeration by timing: an unknown user returns immediately (`AuthService.cs:98-102`). | Verify against a dummy hash when the account is missing. |
| SEC-3 | Medium | PBKDF2 at 100k iterations, with the count not stored (`server/Carbon.Infrastructure/Security/Pbkdf2PasswordHasher.cs:16`). | Store algorithm and iterations with the hash, raise to the OWASP level, and rehash on login. |
| SEC-4 | Medium | Hardcoded fallback Postgres credentials `Username=carbon;Password=carbon` (`server/Carbon.Infrastructure/Configuration/HostRuntimeOptions.cs:58`). This violates `do-not-do`. | Fail at startup when Postgres is selected without an explicit connection string. |
| SEC-5 | Low | Character-limit check-then-insert race (`AuthService.cs:138-173`). | Enforce the limit in SQL. |
| SEC-6 | Medium | No wire-level validation: parallel arrays without length checks (`EntityDeltaBatch.cs:38-63`, `PlayerStatsSync.cs:87-92`, `MapChunkDelta.cs:43-92`); unbounded C2S strings (`AuthLogin.cs`, `AuthRegister.cs`, `CharacterCreate.cs`); signed `int` quantities (`DropInventoryItem.cs:25-26`). | A pure `IsWellFormed()` per packet (lengths, max counts, max string length), called once at dispatch. Use unsigned quantities or reject ≤ 0 in handlers. |

## 9. Wire protocol / contracts

Verified clean: no duplicate opcodes, `[SerializeOrder]` contiguous, no static state, dependencies only on Nalix, namespaces match folders.

| ID | Sev | Finding | Fix |
| :--- | :--- | :--- | :--- |
| WP-1 | High | `ProgressionBlob` — the server's DB persistence format — is sent on the wire unchanged (`CharacterEnterWorld.cs:122-123`, `PlayerStatsSync.cs:87-88`) and parsed on the client (`MultiplayerSession.Packets.cs:410,852`). A migrator change (`SqliteSchemaMigrator.cs:325`) breaks the protocol without a `CarbonProtocol.Version` bump. | ADR: replace it with explicit fields (`ushort[] AttributeIds/Values`, `SkillIds/Ranks`), or version the blob and tie that version to the protocol version. |
| WP-2 | Medium | Opcode bands disagree with the `carbon-packets` skill table (C2S entity input at 0x1201-0x1204 sits in the S2C snapshot band; combat at 0x1233). The band test omits `SharedOpCode` (`tests/contracts/Carbon.Contracts.Tests/ProtocolContractGuardTests.cs:35-41`). | ADR to make per-module bands official and update the skill. Add the `SharedOpCode` row. |
| WP-3 | Medium | Nothing enforces append-only `[SerializeOrder]`; renumbering passes (`ProtocolContractGuardTests.cs:81-104`). | A committed golden snapshot of (packet, order, name, type) that fails on non-append changes. Assert every public property has `[SerializeOrder]` or `[SerializeIgnore]`. |
| WP-4 | Low | `OpStatus` gap at 7 is not marked reserved (`contracts/Carbon.Contracts.Common/OpStatus.cs:29-33`). | Add a "reserved, never reuse" comment. |

## 10. Dead code & duplication

| ID | Sev | Finding | Fix |
| :--- | :--- | :--- | :--- |
| DUP-1 | Medium | Harvest and placement logic exists twice and has already diverged (`AuthoritativeWorld.WorldPropsAndBuilding.cs:24-140` vs `:598-680`; `:143-240` vs `:365-424`). The equipment-snapshot block is copy-pasted five times (`InventoryAndLoot.cs:76-85,246-255,380-389,472-481`; `AuthoritativeWorld.cs:950-964`). | One `HarvestService.Strike`, one `BuildingService.Place`, and an `EquipmentSnapshot.From(state)` value object. |
| DUP-2 | Medium | Move-input → velocity is duplicated between server sim and client prediction (`WorldSimulation.cs:663-676`, `engine/Carbon.Prediction/Reconciliation/PredictionReconciler.cs:108-117`); the replay skips collision and CC. | One shared `MovementSystem.ApplyMoveInput`. |
| DUP-3 | Medium | `CharacterEnterWorld` is applied twice, and the copies already differ (`MultiplayerSession.Auth.cs:257-300` vs `Packets.cs:384-425`). Item stat lines are formatted in four places (`HotbarAndItemWidget.cs:390-398`, `InventoryModalWidget.cs:388-398`, `CraftingModalWidget.cs:507-530`). | One `ApplyEnterWorldLocked` and one `ItemStatFormatter`. |
| DUP-4 | Low | `FloorDiv` is copied in six engine files (`LandmarkStage.cs:43`, `WorldCollision.cs:622`, `RiverNetwork.cs:837`, `PointOfInterestResolver.cs:364`, `TopographyHelper.cs:176`, `RoadNetwork.cs:738`). SplitMix64 appears three times (`StageSeed.cs:28`, `DeterministicRandom.cs:100-103`, `SpatialHash.cs:18-19`). The tile hash `(x*73856093)^(y*19349663)` is inlined five times in desktop. Color `Mix`/`Shade`/`FillEllipse` are duplicated across `WorldTreeArt.cs`, `CloudSystem.cs` and `PointOfInterestRenderer.cs`. | Move them to `DeterministicMath`/`SpatialHash` and `ColorRgba.Lerp/Scale`/`GeometryBatch.FillEllipse`. |
| DUP-5 | Low | Dead: `WorldOutboundSink.AttachHub` (`server/Carbon.Host/WorldOutboundSink.cs:42-43`) is never called. The `IAuthoritativeWorld` registration (`Startup.cs:43`) is unused. A test-only overload `TryUpgradeInventoryItem(..., deterministicRollSeed)` is public (`InventoryAndLoot.cs:305`). `LootDropComponent.OwnerEntityId` is never read. Item-ID-keyed visuals in `ModularCharacterOverlays.cs:182-506` contradict ADR-007 §2. | Delete the dead members, inject `IRandomSource`, and move the visual variants into item `.cvg.json`. |
| DUP-6 | Low | Fonts, UI skin and vector art are reloaded per scene (`LoginScene.cs:95-96`, `CharacterSelectScene.cs:131-133`, `HudOverlayRenderer.cs:60-61`, `WorldSceneRenderer.cs:83`). | A shared `ClientAssets` service. |
| DUP-7 | Low | Shader leak on a fragment-stage compile error (`graphics/Carbon.Graphics.OpenGL/Shaders/Shader.cs:38-39`). | Delete the vertex shader before rethrowing. |

`graphics/` is otherwise clean: no gameplay concepts, no hot-path allocations, and GL handles are disposed.

## 11. Determinism

| ID | Sev | Finding | Fix |
| :--- | :--- | :--- | :--- |
| DET-1 | Low | `ChunkDeltaPacketMapper` iterates `Dictionary` into packets (`engine/Carbon.Maps/Networking/ChunkDeltaPacketMapper.cs:38,54,70`), while `ChunkDelta.Encode` deliberately sorts (`ChunkDelta.cs:257-259`). | Sort keys before mapping. |
| DET-2 | Low | Commands apply in network-arrival order, and `SimulationCommand.ClientTick` is ignored (`WorldSimulation.cs:39,654`). | Stable-sort each tick's commands by (entity, ClientTick, sequence). |
| DET-3 | Medium | Entity separation runs after world collision and pushes bodies into walls, and also pushes static props (`engine/Carbon.Simulation/ECS/Systems/EntityCollisionSystem.cs:147-155`). | Re-resolve against world physics after separation; weight by mobility. |

## 12. Test gaps

Before this PR: 299 `[Fact]`/`[Theory]` methods (335 executed test cases). This PR adds 24 architecture tests (359 total, all green).

| Production area | Direct tests | Gap |
| :--- | :--- | :--- |
| `server/Carbon.Application` (AuthoritativeWorld, 4.4k lines) | 3 end-to-end on real SQLite | **TEST-1 (Critical):** no per-partial unit tests (cooldowns, loot expiry, building, AoI enter/leave, point allocation). `SpatialAoiTracker` is untested. → New `Carbon.Application.Tests` with in-memory store fakes and `StepFixedTick()` stepping. |
| `server/Carbon.Handlers` | 0 | **TEST-2 (High):** no auth rejection, reply opcode or malformed-input tests. → `Carbon.Handlers.Tests` with NSubstitute `IWorld*` and `IPacketContext<T>` fakes. |
| `server/Carbon.Infrastructure` (Postgres, migrators) | SQLite fresh-DB only | **TEST-3 (High):** no Postgres tests; no upgrade-path test (`SqliteSchemaMigrator.cs:148-251,325`). → Seed an old schema, migrate twice, assert the blob; Postgres behind an opt-in trait. |
| `desktop/Carbon.Desktop.Networking` (3.4k lines) | 1 live-socket test | **TEST-4 (High):** no packet-apply tests; it uses real-time `Task.Delay` polling (`tests/desktop/Carbon.Desktop.Tests/MultiplayerSessionTests.cs:172-297`), which will flake in CI. → Factor the apply logic out; use an in-process loopback and condition waits. |
| `server/Carbon.Host` (`WorldOutboundSink`, `WorldTickHostedRunner`) | 0 direct | **TEST-5 (Medium):** pooled rent/return and fan-out to disconnected sessions are untested. |
| Wall clock in tests | — | **TEST-6 (Medium):** `DateTimeOffset.UtcNow` in `tests/server/Carbon.Host.Tests/AuthoritativeWorldMultiplayerTests.cs:53,143,308,387,496` and `SqlitePersistenceRoundTripTests.cs:87`. → Fixed instants or `FakeTimeProvider`. |
| Style | — | **TEST-7 (Low):** 23 of 78 test files use xUnit `Assert` instead of FluentAssertions (all of `tests/engine/Carbon.Maps.Tests`). 86 of 299 test names don't follow `Method_Scenario_Expected`. → Migrate when touched. |

## 13. Automated guards added in this PR

Project: `tests/architecture/Carbon.Architecture.Tests` · Solution: `tests/Carbon.Architecture.Tests.slnx` · CI step: **Test architecture** in `.github/workflows/ci.yml`. Reflection/metadata based; xUnit + FluentAssertions only. NetArchTest is not in `Directory.Packages.props`, so no new framework was added.

| Test class | Rule | Existing violations |
| :--- | :--- | :--- |
| `LayerDependencyTests` | Layer graph per AGENTS.md rule 3 (contracts → nothing; engine → contracts; graphics → contracts/engine; server never → graphics/desktop; desktop never → server), checked on both `.csproj` and compiled metadata. Restricted packages stay in their layer. Every production project must be referenced by the architecture test project. | none |
| `EngineDesignTests` | ECS components are unmanaged structs with no behavior methods. Public engine classes are sealed/abstract/static unless they are inherited. | 1 allowlisted (ECS-5) |
| `CodeHygieneTests` | No `async void` in any production assembly. No static mutable state in engine/server (non-readonly statics, or `static readonly` mutable collections). Namespace = RootNamespace + folder path for all 454 source files. | 21 statics allowlisted with reasons |
| `ResourceTests` | Every `resources/**/*.json` parses strictly. `resources/art` loads through the real `CarbonVectorArtLibrary.LoadFromDirectory`. Every `.cvg.json` is referenced by the catalog (otherwise it is never validated), and every reference exists. Raster images only under `resources/ui` and `resources/fonts`. Nothing under `resources/thirdparty`. | none |
| `ServerAuthorityTests` | Desktop must not use *new* engine balance types (`StarterGameCatalog`, `SkillCatalog`, …). No *new* gameplay-named numeric `const`/`static readonly` (speed, damage, cooldown, price, …) in desktop. Heuristic. | 36 usages + 3 constants allowlisted (SA-1, SA-3) |

Every allowlist has a matching `*_ShouldNotContainStaleEntries` test, so fixing a violation forces you to delete its entry, and the list only shrinks.

### Suggested follow-ups to the guards (after the fixes above)

- Extend the static-mutable-state rule to `desktop/` once TS-11 is fixed.
- Once LB-1 splits the balance assembly, turn `ServerAuthorityTests` into a hard layer rule (desktop → balance assembly forbidden).
- Add a golden `[SerializeOrder]` snapshot test (WP-3).

## Recommended order of work

1. **Now (small, security):** SA-8 harvest checks, SEC-4 default credentials, TS-4 context-after-await, SEC-1 login throttling, LB-1 dead references.
2. **With the in-flight config-sync work:** SA-1…SA-7 via `GameConfigSync`, plus DDD-1 (tables to JSON). These share one ADR.
3. **After the gameplay PRs land:** SOLID-1 decomposition, one service per PR, each with a new `Carbon.Application.Tests` suite (TEST-1). TS-1 (command queue) falls out of this.
4. **Client:** TS-2 main-thread inbox, then SOLID-2, HP-5…HP-7.
5. **Engine:** TS-6 chunk recycling, ECS-1/ECS-2 combat pipeline (with the combat/AI owners), HP-8…HP-11.

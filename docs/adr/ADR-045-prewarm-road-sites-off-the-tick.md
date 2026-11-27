# ADR-045: Plan Road Sites Off the Tick Thread

- **Date**: 2026-11-27
- **Status**: Accepted (by the orchestrator: D1–D6 and D8 as proposed, D7 with a per-world cache, D9 and D10 added)
- **Follows**: audit round 3 ALLOC-12 (#228), which left road planning on the tick as the largest chunk-streaming cost
- **Scope**: `engine/Carbon.Maps` (`RoadNetwork`, `ProceduralWorldGenerator`, `ChunkWorld2D.UpdateAroundPosition`), `tests/engine`

## Context

A chunk is generated when a player's streaming window reaches it: every 10 ticks on the server
(`AuthoritativeWorld.TickAndAoi.cs:165`, load radius 2) and in the client's frame update (`MultiplayerSession.cs:825`,
radius 4). `RoadStage` asks `RoadNetwork.Query` for the roads crossing the chunk. `Query` builds every road site within
`Reach + 1` sites of the chunk that is not cached yet (`RoadNetwork.GetSiteData`): an A* route to each linked site, up
to 420 tiles long, sampling terrain and river distances along the way. All of it runs on the calling thread.

Hydrology already avoids that. `UpdateAroundPosition` calls `PrewarmHydrology` with a radius of
`(chunkRadius + 8) × 32` tiles (320 on the server). That starts the missing river regions (512 tiles each) on the thread
pool as `Lazy<RegionData>` (`RiverNetwork.Prewarm`). But a road route reaches up to two sites (448 tiles) plus 420 tiles
from the chunk, beyond that radius. So the road planner builds the river regions it needs **synchronously**, inside
`RoadStage`.

### Measured tick-thread time today

Seed 1337 walks east one chunk per step with the server's radii (load 2, evict 8). The table shows the time of one
`UpdateAroundPosition` call, and the part spent in `RoadStage`. Two runs, on a quiet machine.

| step | cold process (JIT, caches) | JIT warm, new seed (9256, 17175) | same seed again (hydrology cached, roads re-planned) |
|---|---|---|---|
| 0 (first area) | 377–708 ms (road 303–580) | 175–205 ms (road 162–187) | 35–45 ms (road 12–13) |
| 6 | 138–319 ms (road 133–309) | 19–22 ms (road 17–20) | 13–19 ms (road 8–12) |
| 13 | — | 34–78 ms (road 30–75) | — |
| 27 | 47–102 ms (road 43–94) | 48–59 ms (road 45–56) | 6–9 ms (road 2–3) |
| 34 | 31–66 ms (road 27–59) | 5–21 ms (road 3–18) | 8–11 ms (road 4–5) |
| other steps | 5–7 ms | 4–7 ms | 5 ms |

What this shows:
- A tick at 30 Hz has 33 ms. A player entering an unexplored area stalls the server tick for **20–200 ms**, everyone's
  tick, with the JIT warm.
- **Most of the road time is hydrology** that road routing pulls in synchronously. Routing alone costs 2–13 ms per site.
- The allocations are large in proportion: 0.6–4.9 MB per new site (ALLOC-12).
- A step with no new road site still costs about 5 ms for five chunk builds. That is not part of this ADR.

### Properties the change can rely on

- **Site data is a pure function of the seed and the site.** `BuildSiteData` and `Choose` read only the seed, the
  deterministic landmark resolver, terrain samples and river distances, which are themselves pure and cached. Two builds
  of one site, on any threads, give equal results. The cache already tolerates a racing duplicate: `TryAdd` keeps the
  first copy, and both are equal.
- The cache (`_bySite`, `_choices`, bounded at 2,048 sites, trimmed first in, first out) belongs to the generator
  instance and is already a `ConcurrentDictionary`.
- Hydrology's cache is process-wide (`RiverNetwork.s_sharedRegions`, 128 regions). Regions trimmed from it are rebuilt
  when needed again (#228 found this through a test).

## Decision

1. **D1 — Prewarm road sites.** `UpdateAroundPosition` calls a new `PrewarmRoads` next to `PrewarmHydrology`. It starts,
   on the thread pool, the build of every road site that chunks within `chunkRadius + K` of the centre would query. `K`
   comes from config and defaults to two chunks; a site is 7 chunks wide. The builds start only for sites not cached and not in flight. The prewarm
   happens once per chunk-boundary crossing, as `Stream` does.
2. **D2 — One build per site, joined, never duplicated.** The site cache holds `Lazy<RoadSiteData>` with
   `ExecutionAndPublication`, as hydrology does. If a chunk needs a site whose prewarm has not finished, the tick
   **joins** the in-flight build: it waits for the rest of it, with no second build. A site never prewarmed is built on
   the tick exactly as today. So the worst case is today's cost and the usual case is none. A no-roads fallback is
   rejected (see Alternatives Considered).
3. **D3 — Determinism is unchanged and tested.**
   - Building sites concurrently, in shuffled order, gives `RoadLink` arrays and ramps equal to a sequential build.
   - Generating a strip with prewarm on, and with it off, gives byte-identical chunks (tiles, roads, bridges, props).
4. **D4 — Hydrology for roads is prewarmed too.** The road prewarm task touches the river regions its routes need inside
   the background build, so the river regions are built off the tick as well. No change to hydrology's own prewarm
   radius.
5. **D5 — Bounded background work.**
   - At most `N` road builds run at once. `N` comes from config and defaults to 2. They go through a small limiter, not one unbounded `Task.Run`
     per site, so a player sprinting through new land does not flood the thread pool the server's network I/O uses.
   - Queued sites farther than the prewarm radius from every caller's latest centre are dropped before they start.
6. **D6 — Ownership.** The cache stays per generator instance. Trimming removes only completed entries, so a joined
   build is never dropped under the waiting tick.
7. **D7 — A per-world hydrology cache, kept warm under present players.** The road prewarm keeps the river regions its
   routes need warm, but #228 found a second way a region gets rebuilt on the tick. The process-wide cache
   (`RiverNetwork.s_sharedRegions`, 128 regions, trimmed first in, first out) is shared by every world in the process.
   Once the players online span more regions than that, or another world in the process (tests, a future multi-world
   server) fills it, a region is trimmed and rebuilt (0.6–2.4 MB) the next time a chunk needs it, on the tick if its
   prewarm has not finished. Three parts:
   - **Per world.** The region and block caches move from statics into the generator's `RiverNetwork` instance, owned by
     the world's generator. Worlds and tests no longer trim each other's regions, and the rules below reason about one
     world's players only. A region is still built once per world and shared by everything in that world.
   - **Never trim a region in use.** A region within the prewarm radius of any online player's latest centre is pinned;
     trimming skips pinned regions. So the cache can no longer evict what the players need, however many are online.
   - **Size the bound against the players.** The bound becomes the larger of 128 and the regions all players' prewarm
     radii cover, plus a margin. A prewarm radius of `r` tiles touches at most `(2r / 512 + 1)²` regions of 512 tiles:
     9 at hydrology's 320 tiles today, about 25 if it covers the roads' reach (about 900 tiles). Players in the same
     area share regions. Memory grows with the spread of the players, not with time.
   - **Guard:** with players spread over more than 128 regions, stepping each player again rebuilds no region on the
     calling thread. A second world in the same process leaves the first's cache untouched.
8. **D8 — Guards.**
   - A tick-time test: after a prewarm and an explicit wait-for-idle hook, a step into the prewarmed area spends **0 ms in
     road planning**, measured as road builds on the calling thread = 0, not as wall time.
   - The allocation test from #228 extended to a cold strip: the step allocates nothing in `RoadStage` once its sites
     are prewarmed.
9. **D9 — Background builds never take the world lock.** A road or region build reads only pure inputs (the seed,
   terrain samples, landmarks, river distances) and publishes only into its concurrent cache. It never takes
   `AuthoritativeWorld`'s lock or `ChunkWorld2D`'s gate, and never touches chunks, entities or deltas. So a build can
   never deadlock with, or be blocked by, the tick. **Test:** with the world lock held by the test thread, prewarmed
   builds still complete.
10. **D10 — Clean shutdown.** On server shutdown, after the tick has stopped, the host calls
    `AuthoritativeWorld.StopBackgroundBuilds`, which reaches the generator's background builds. Builds not started yet
    are skipped, and running ones stop at their next check. Every background task is observed, and the stop waits for
    them, so no `TaskScheduler.UnobservedTaskException` is raised. A caller joining a build cancelled by shutdown gets an
    `OperationCanceledException` it does not catch, because the world is stopping. **Test:** stop a network while
    builds are queued and running; all tasks finish, the queued ones are skipped, and no unobserved exception is raised
    (asserted through `UnobservedTaskException` after a forced collection).
    - **Amended in implementation:** an explicit stop instead of disposing the generator. A disposable generator would
      have to be disposed at its 149 creation sites (CA2000 is an error), and a `CancellationTokenSource` field would
      make it disposable too (CA1001). A stop flag the builds check gives the same behaviour.

## Alternatives Considered

- **Fall back to "no roads yet" when a site is not ready** (rejected). The tick would never wait, but a chunk's contents
  would depend on thread timing. The same seed could give a chunk with or without its road, and the client and server,
  which generate the same chunks, would disagree. Joining the in-flight build (D2) keeps chunks a pure function of the
  seed.
- **Raise `PrewarmHydrology`'s radius to cover road routes** (about 900 tiles). It moves the hydrology part off the tick
  but not the road A*. It also builds more river regions than the roads need, into a bounded cache that would then trim
  more.
- **Plan roads at world start for the whole map.** The world is unbounded, so there is no finite whole map.
- **Precompute roads offline into resources.** That needs a fixed world size and a build step, against the seeded,
  procedural world (no ADR supports a fixed map).
- **Time-slice road planning across ticks.** The planner would become resumable, and the tick would still wait the full
  time when a chunk is needed now.

## Consequences

- An unexplored area no longer stalls the server tick for road planning once its prewarm has run, and a region the
  players need is no longer trimmed and rebuilt under them (D7). The prewarm starts as
  the player crosses into the neighbouring chunks, a few seconds ahead at walking speed. A sprint or a teleport into
  unexplored land can still join an in-flight build and wait for its rest.
- Background CPU use rises a little: sites near a player are planned whether or not the player ends up there.
- Generator code gains a second concurrent cache. D2's `Lazy` and D3's tests keep it deterministic.
- The host stops the generator's background builds on shutdown (D10).
- Generators with the same seed no longer share regions (D7). The production server has one world. The desktop client
  builds its menu backdrop's generator and its session's separately, so entering the world builds the backdrop's
  regions again. The test suites build many same-seed generators: Maps took 105 s instead of 85 s, Host 37 s instead
  of 25 s.
- A world's hydrology memory is its own (D7). Two worlds in one process each keep their regions instead of sharing
  and trimming one cache.
- The client streams with the same generator, so its frame hitches on new land shrink too.

## Implementation

In small PRs, each characterised first:
1. Per-world hydrology cache plus pinning and the sized bound (D7), with D9 and D10 for region builds.
2. Road-site prewarm (D1, D2, D4–D6, D9, D10), with the D3 determinism tests and the D8 guards.

## Decisions taken

- Joining the in-flight build (D2) is accepted; the no-roads fallback is rejected for determinism.
- `N` = 2 and `K` = 2 chunks, both from config.
- The hydrology cache becomes per world (D7), instead of pinning inside a process-wide cache.
- D9 (no world lock) and D10 (clean shutdown) were added on review.

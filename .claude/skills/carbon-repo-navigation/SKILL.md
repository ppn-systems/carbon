---
name: carbon-repo-navigation
description: Use to locate code, decide where a new file or project belongs, or navigate between server, engine, contracts, client runners, and docs across the 11 Carbon folders — analyzer, contracts, deploy, desktop, docs, engine, graphics, library, resources, server, wasm.
---

# Carbon Repository Navigation

Carbon is organized into 10 decoupled directories to maintain clear architectural boundaries and support both Dedicated Server and cross-platform clients.

## Directory Map & Responsibilities

```
carbon/
├── analyzer/     # Roslyn incremental analyzers & source generators
├── contracts/    # Shared networking protocols, DTOs, opcodes, and game event definitions
├── deploy/       # Docker, docker-compose, Kubernetes manifests, and CI/CD pipelines
├── desktop/      # Cross-platform native desktop client runner
├── docs/         # Architecture specifications, ADRs, execution prompts, and protocol specs
├── engine/       # Core game simulation, ECS, fixed-tick loop, spatial hashing, and collision
├── graphics/     # Client-only 2.5D rendering modules (Carbon.Graphics.*: OpenGL, batching, fonts, scenes, UI)
├── library/      # Pre-built managed assemblies and dependencies (e.g. Nalix.Observability)
├── resources/    # Game configurations, schemas, maps, tilesets, assets, and localization
├── server/       # Dedicated game server, gateway multiplexer, world simulation, and auth
└── wasm/         # WebAssembly client target for browser execution
```

## Layer Boundaries & References

- **`contracts/`**: Depends on NOTHING. Pure contracts, DTOs, packet definitions, serialization markers.
- **`engine/`**: Depends ONLY on `contracts/`. Pure simulation, ECS components and systems, spatial partitioning, deterministic math.
- **`graphics/`**: Client-only rendering (Carbon.Graphics.*, Silk.NET.OpenGL). May depend on `contracts/` and `engine/`. Never referenced by `server/`.
- **`server/`**: Depends on `contracts/`, `engine/`, and server-side transports. Never references `graphics/` or client runners.
- **`desktop/` & `wasm/`**: Depend on `contracts/`, `engine/` (for client prediction and simulation replay), and `graphics/` (rendering).
- **`analyzer/`**: Analyzers and Source Generators. Referenced as `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`.
- **`resources/`**: Static and data-driven assets consumed at runtime (JSON schemas, tile maps, localization).
- **`library/`**: Third-party pre-compiled assemblies (read-only).

## Documentation Navigation (`docs/`)

- `docs/adr/`: **Authoritative Architecture Decision Records**. Check here first before designing new systems or changing protocols.
- `docs/execution/`: Active execution prompts drafted by `prompt-author` and executed by `prompt-execute`.
- `docs/archive/`: Historical records, completed execution logs, and superseded ADRs. Never implement from archive.

## How to Locate a Game Feature

1. **Packet / Opcode**: one project per domain, `contracts/Carbon.Contracts.<Domain>/` (`Common`, `Config`, `Entities`,
   `Identity`, `Items`, `Maps`). Client requests are in `C2S/`, server messages in `S2C/`, and the opcodes in
   `<Domain>OpCode.cs` (for example `MapOpCode.ChunkRequest`, `ItemOpCode`, `SharedOpCode`).
2. **Server Packet Handler**: `server/Carbon.Handlers/<Domain>/<Domain>Handlers*.cs`, matched by
   `[PacketOpcode(<Domain>OpCode.<Name>)]`.
3. **Server Game Logic**: the `AuthoritativeWorld` partials in `server/Carbon.Application/World/` (`AuthoritativeWorld.<Area>.cs`)
   and the services under `server/Carbon.Application/World/<Area>/` (`Combat/`, `Commerce/`, `Inventory/`, `Loot/`,
   `Persistence/`, `Replication/`, ...).
4. **Shared Rules / ECS**: pure rules live in the domain projects under `engine/` (`Carbon.Items`, `Carbon.Combat`,
   `Carbon.Maps`, `Carbon.Survival`, `Carbon.Economy`, ...). ECS struct components are in
   `engine/Carbon.Simulation/ECS/Components/` (`Transform2D`, `Velocity2D`, ...) and systems in
   `engine/Carbon.Simulation/ECS/Systems/` (`MovementSystem`, `EntityCollisionSystem`).
5. **Desktop Client**: scenes in `desktop/Carbon.Desktop/Scenes/`, gameplay input and interactions in
   `desktop/Carbon.Desktop/Gameplay/` and `desktop/Carbon.Desktop/Input/`, rendering and HUD in
   `desktop/Carbon.Desktop.Rendering/`, and the network session in `desktop/Carbon.Desktop.Networking/Session/`.
   `wasm/` holds no code yet.

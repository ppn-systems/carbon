# Carbon

Carbon is a high-performance, modular C# / .NET MMORPG engine and server-client framework designed for low latency, cross-platform gameplay (Desktop & WebAssembly).

## Project Structure

```
carbon/
├── analyzer/     # Roslyn analyzers, source generators (packet serializers, RPC generators, ECS codegen)
├── contracts/    # Shared networking protocols, DTOs, messages, RPC contracts, and game events
├── deploy/       # Docker, docker-compose, Kubernetes manifests, and CI/CD automation
├── desktop/      # Cross-platform desktop client runner
├── docs/         # Architecture specifications, ADRs, protocol docs, and system design
├── engine/       # Core game simulation, ECS, fixed-tick loop, spatial hashing, and collision
├── library/      # Pre-built managed assemblies and dependencies (e.g. Nalix Observability)
├── resources/    # Game configurations, schemas, maps, tilesets, assets, and localization
├── server/       # Dedicated game server, gateway multiplexer, world simulation, and auth
└── wasm/         # WebAssembly client target for browser execution
```

## Getting Started

*(Documentation and build instructions will be updated as the core modules are implemented.)*

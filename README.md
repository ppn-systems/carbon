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

**Prerequisites:** .NET SDK 10.0.402 or a later 10.0 feature band (see `global.json`).

Build the engine:

```bash
dotnet build engine/Carbon.Graphics.slnx -c Release
```

Run the tests:

```bash
dotnet test tests/Carbon.Graphics.Tests.slnx -c Release
```

The build treats warnings as errors. CI (`.github/workflows/ci.yml`) runs the same build and tests on every pull request.

> Only the `engine/` graphics modules (Carbon.Graphics.*) are implemented so far; the other folders are placeholders.

# Carbon

Carbon is a high-performance, modular C# / .NET MMORPG engine and server-client framework designed for low latency, cross-platform gameplay (Desktop & WebAssembly).

## Project Structure

```
carbon/
├── analyzer/     # Roslyn analyzers, source generators (upcoming)
├── contracts/    # Shared networking protocols, DTOs, opcodes (Carbon.Contracts.*)
├── deploy/       # Docker, docker-compose, Kubernetes manifests, CI/CD (upcoming)
├── desktop/      # Cross-platform desktop client runner (Carbon.Desktop)
├── docs/         # Architecture specifications, ADRs, protocol docs, and system design
├── engine/       # Core game simulation, procedural maps, physics, ECS (Carbon.Maps, Carbon.Physics, Carbon.Simulation)
├── graphics/     # Client-only 2.5D rendering modules (Carbon.Graphics.*: OpenGL, batching, fonts, scenes, UI)
├── library/      # Pre-built managed assemblies and dependencies (e.g. Nalix Observability)
├── resources/    # Game configurations, schemas, maps, tilesets, assets, and localization (upcoming)
├── server/       # Dedicated game server, gateway multiplexer, world simulation, and auth (upcoming)
└── wasm/         # WebAssembly client target for browser execution (upcoming)
```

## Getting Started

**Prerequisites:** .NET SDK 10.0.402 or a later 10.0 feature band (see `global.json`).

Build and test the entire workspace:

```bash
dotnet build Carbon.slnx -c Release
dotnet test Carbon.slnx -c Release
```

Or build and test individual layers:

```bash
dotnet build contracts/Carbon.Contracts.slnx -c Release
dotnet build engine/Carbon.Engine.slnx -c Release
dotnet build graphics/Carbon.Graphics.slnx -c Release
dotnet build desktop/Carbon.Desktop.slnx -c Release

dotnet test tests/Carbon.Contracts.Tests.slnx -c Release
dotnet test tests/Carbon.Engine.Tests.slnx -c Release
dotnet test tests/Carbon.Graphics.Tests.slnx -c Release
```

Run the native desktop sandbox viewer:

```bash
dotnet run --project desktop/Carbon.Desktop -c Release
```

The build treats warnings as errors (`TreatWarningsAsErrors`). CI (`.github/workflows/ci.yml`) builds `contracts/`, `engine/`, `graphics/`, and `desktop/`, and runs all test suites on every pull request.

> **Current Status:** `contracts/`, `engine/`, `graphics/`, and `desktop/` are implemented. `server/`, `analyzer/`, `wasm/`, `resources/`, and `deploy/` are upcoming targets.

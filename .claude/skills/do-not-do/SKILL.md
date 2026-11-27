---
name: do-not-do
description: Use as a rapid prohibition check when unsure whether an action is allowed in Carbon — architectural bans, network protocol restrictions, hot-path allocation bans, concurrency anti-patterns, and git discipline.
---

# Do-Not-Do: Prohibitions for Carbon

If an action is listed in this table, STOP. An exception requires written reasoning, explicit human approval, and an Architecture Decision Record (ADR) in `docs/adr/`.

| Do Not | Instead | Owning Skill |
| :--- | :--- | :--- |
| **Commit directly to `master`** | Create a `feature/*` or `fix/*` branch and submit a PR | `review-gates` |
| **Change the architecture, add third-party frameworks, or change protocol stack without an ADR** | Document proposal and write an ADR in `docs/adr/` first | `review-gates` |
| **Modify binaries in `library/` from a Carbon task** | `library/` contains upstream assemblies (e.g. Nalix.Observability); treat as read-only | `csharp-style` |
| **Allocate objects or use LINQ inside steady-state tick simulation loop** | Use struct components, `Span<T>`, and object pooling (`PacketPool.Rent<T>()` for outbound packets, ADR-043) | `performance-aot` |
| **Renumber or reuse existing `[SerializeOrder]` or Opcode numbers** | Append new members with higher order numbers; preserve backwards compatibility | `carbon-packets` |
| **Directly mutate Entity state across threads outside the World Tick loop** | Enqueue incoming client commands to the World Tick queue; process sequentially in tick | `carbon-handlers` |
| **Hardcode credentials, JWT secrets, database connection strings, or private keys** | Use environment variables, configuration providers, or secret managers | `csharp-style` |
| **Use `Thread.Sleep`, unconstrained delays, or wall-clock time in unit tests** | Use deterministic time steppers, virtual clocks, and async completion signals | `testing-style` |
| **Rely on runtime reflection (`Reflection.Emit`, runtime JSON) in hot paths** | Use Roslyn source generators and static generic constraints compatible with Native AOT | `performance-aot` |
| **Broadcast full world state indiscriminately to all connected players** | Use Spatial Partitioning (AoI grid) to only send snapshots of nearby entities | `carbon-network` |
| **Suppress compiler warnings with `#pragma warning disable` without explicit approval** | Fix the root cause; maintain zero warnings under `TreatWarningsAsErrors` | `csharp-style` |

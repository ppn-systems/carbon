---
name: review-gates
description: Use before committing, opening a PR, or declaring any task complete in Carbon — branch workflow, commit message standards, scoped build/test verification, do-not-merge conditions, and concurrency rules for agents.
---

# Review Gates & PR Standards for Carbon

## Workflow Mechanics

- **Never commit directly to `master`.**
- Always create a dedicated branch off `origin/master`:
  - `feature/<name>` for new features or modules.
  - `fix/<name>` for bug fixes.
  - `chore/<name>` for tooling, dependencies, and scaffolding.
- One concern per commit and per Pull Request.
- Every commit must compile cleanly with zero errors and zero warnings.

## Mandatory Gate: Run What CI Runs

Before **every push** to a PR branch, run:

```bash
tools/ci/verify.sh --merge-master
```

It merges your `HEAD` onto the latest `origin/master` in a throwaway worktree, deletes `artifacts/`, and runs every `dotnet build` / `dotnet test` step of `.github/workflows/ci.yml` in order with `-warnaserror`. That is exactly what GitHub tests (the PR merge commit), so a green run means a green PR.

- A merge conflict fails the gate: `git merge origin/master` into your branch and resolve it first. For additive hotspots (`tools/screenshots/scenarios.txt`, env knobs, debug overrides, `.ini` sections, allowlists) keep both sides.
- Never push with `--no-build` or single-solution results. Running only `tests/Carbon.Architecture.Tests.slnx` misses server allocation budgets, and running only the server tests misses the i18n, size-cap and layer guards.
- A guard that fails after the merge (allocation budget, class-size cap, literal allowlist, ADR or opcode uniqueness) is a real finding. Fix the cause; never raise a cap or add an allowlist line to get green.
- Re-run after another PR lands on `master` before you ask for a merge.

## Scoped Verification Gates

While iterating (not as a substitute for the mandatory gate), these scoped runs give faster feedback:

| Change Scope | Required Verification |
| :--- | :--- |
| **Documentation / Markdown only** | Verify markdown formatting, link integrity, and grammar. |
| **Contracts / Protocol (`contracts/`)** | Build `contracts/` and all consumers (`engine/`, `server/`, `desktop/`, `wasm/`). Verify packet opcode uniqueness and serialization orders. |
| **Core Engine (`engine/`)** | Build `engine/` and run engine unit tests (tick determinism, math, ECS iteration). |
| **Graphics (`graphics/`)** | Build `graphics/Carbon.Graphics.slnx` and run `tests/Carbon.Graphics.Tests.slnx`. |
| **Server / Network (`server/`)** | Build `server/` and run server integration/packet handler tests. |
| **Analyzers / Generators (`analyzer/`)** | Verify generator executes during consuming project builds and emitted output matches expectation without breaking IDE responsiveness. |

## Do NOT Merge If:

- Build fails or produces any compiler warnings (`TreatWarningsAsErrors`).
- Code is committed directly to `master`.
- A packet member was removed or renumbered in a breaking manner without protocol version bump.
- Any secret, credential, private key, or token is hardcoded in source.
- Tests rely on non-deterministic delays (`Thread.Sleep`) or wall-clock timestamps.
- Binary assemblies outside `library/` are checked into git without explicit authorization.

## Commit Convention

Follow Conventional Commits: `<type>(<scope>): <subject>`
- Types: `feat`, `fix`, `refactor`, `perf`, `test`, `docs`, `chore`.
- Scope: `contracts`, `engine`, `graphics`, `server`, `network`, `analyzer`, `desktop`, `wasm`, `deploy`.
- Subject: Imperative, lowercase, concise (≤ 72 characters).
- Example:
  - `feat(contracts): define player movement c2s packet and opcode`
  - `perf(engine): optimize spatial grid hash calculation for entity aoi`
  - `fix(server): resolve race condition in connection session cleanup`

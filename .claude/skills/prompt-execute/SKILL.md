---
name: prompt-execute
description: Use when asked to run, execute, or "chạy" one or more numbered execution prompts from docs/execution/ in Carbon (e.g. "run 1", "chạy prompt 1") — pre-flight verification, literal DOD satisfaction, scoped build/test, and branch/PR workflow.
---

# Executing Numbered Prompts in Carbon

Use this skill when instructed to run or execute one or more numbered prompts from `docs/execution/` (e.g., `docs/execution/{N}-*.md`).

## Execution Procedure

### 1. Pre-Flight Checks
- Resolve `{N}` to exactly one file in `docs/execution/`. Read the entire prompt.
- **Verify cited evidence**: Open every `file:line` cited in the prompt's Findings. Confirm that the current code matches the prompt's description.
  - If the code has drifted or the issue is already resolved, **STOP and report the drift**. Do not apply a stale fix blindly.
- Check dependencies: If the prompt specifies prerequisite prompts or commits, verify they exist in git history.

### 2. Isolate Work in a Feature Branch
- Create a dedicated branch off `origin/master`:
  ```bash
  git checkout -b <type>/<N>-<slug>
  ```
  Where `<type>` follows Conventional Commits (`fix/`, `feat/`, `perf/`, `chore/`).

### 3. Implement Exactly the Prompt
- Load domain skills specified in `AGENTS.md` for the touch area (e.g., `carbon-network`, `carbon-handlers`, `performance-aot`).
- Execute tasks in sequential order.
- Respect all `CONSTRAINTS` strictly.
- Make surgical edits: touch only what is required to achieve the goal.

### 4. Verification & Literal DOD
- Ensure every item in the **Definition of Done (DOD)** is satisfied:
  - Run the specific unit/integration tests cited in the prompt.
  - Build the touched projects and verify zero errors and zero warnings (`TreatWarningsAsErrors`).
- If any DOD item cannot be met, name it explicitly as a blocker in the final report.

### 5. Commit & Report
- Commit message format:
  ```
  <type>(<scope>): <subject> [exec/N]
  ```
  Example: `fix(engine): clamp player velocity on diagonal input [exec/1]`
- Report back with:
  - Root cause and solution summary in 1-2 sentences.
  - DOD items verified.
  - Scoped build and test evidence.
  - Changed files.

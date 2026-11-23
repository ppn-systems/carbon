---
name: prompt-author
description: Use when asked to write, draft, or "viết" a new execution prompt for a feature, bug fix, or refactor in Carbon — evidence-first root cause investigation, falsifiable Definition of Done (DOD), constraints, and non-goals. Author agent never executes the prompt it writes.
---

# Authoring Execution Prompts for Carbon

Use this skill when drafting an execution prompt to be executed asynchronously or by another agent. 

**Core Principle:** The authoring agent investigates root cause, writes the structured prompt file in `docs/execution/{N}-{slug}-prompt.md`, and **STOPS**. The author NEVER implements the code or executes the prompt itself.

## Workflow Steps

### 1. Evidence-First Investigation (T0)
- Read actual codebase files across `contracts/`, `engine/`, `server/`, `analyzer/`, `desktop/`, or `wasm/`.
- Cite exact `file:line` for every finding and root cause.
- Distinguish clearly between:
  - **Verified facts**: Code inspected directly.
  - **Suspected assumptions**: Inferred behavior or unverified hypothesis.
- If you cannot find the cause, do not guess: write a diagnostic-first prompt that instructs the executing agent to inspect specific logs or runtime states.

### 2. Determine Prompt Number (T1)
- Prompts are numbered sequentially: `{N}-{slug}-prompt.md` in `docs/execution/`.
- Check existing prompts with `git ls-files 'docs/execution/*.md'` and select the next available integer.

### 3. Write the Prompt File (T2)
Create `docs/execution/{N}-{slug}-prompt.md` following this structure:

```markdown
# Prompt #N — [Concise Title Naming Defect or Feature]

## Findings & Evidence
- Root cause citation: `path/to/file.cs:line`
- Verified: [Observed behavior or code flaw]
- Suspected: [Hypothesis if full trace cannot be completed statically]

> Scope: [Affected directories: engine, server, contracts, etc.]

=== PROMPT START ===

# ROLE
[Specific agent role: e.g. Game Simulation Engineer, Network Protocol Specialist]

# GOAL
[Single clear objective for this prompt]

# TASKS
1. [Task 1]
2. [Task 2]

# CONSTRAINTS
- Do NOT modify files outside [scoped directories].
- Maintain zero allocation in hot-path simulation tick.
- Must preserve backward compatibility in packet serialization.

# DEFINITION OF DONE (DOD)
- [ ] [Falsifiable check 1: e.g. MovementSpeed clamped to max 12.0f when input exceeds bounds]
- [ ] [Falsifiable check 2: Unit test `MovementSystem_SpeedExceedsLimit_ClampsToMax` passes]
- [ ] Build succeeds with zero warnings.

=== PROMPT END ===
```

### 4. Non-Negotiables
- Every DOD item must be **falsifiable** (naming exact expected values or tests), never vague ("feature works correctly").
- **Never execute the prompt after writing it.** Hand back the prompt file path and root cause summary to the user.

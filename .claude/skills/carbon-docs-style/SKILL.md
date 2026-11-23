---
name: carbon-docs-style
description: Use when writing or editing any documentation in Carbon — README, AGENTS.md, skills, ADRs in docs/adr/, execution prompts, and code documentation. Language conventions by audience, ADR structure and lifecycle, and keeping agent docs lean.
---

# Carbon Documentation Style

Applies to all documentation, markdown files, architecture records, and execution prompts.

## Language by Audience

| Location / Document | Primary Language | Notes |
| :--- | :--- | :--- |
| **Code comments & XML docs** | English | Technical precision, standard .NET documentation style |
| **Agent-facing docs** (`AGENTS.md`, `.claude/skills/*`) | English | Maximum precision and consistency across LLM agents |
| **Execution prompts** (`docs/execution/*`) | English | Explicit DOD, verified citations with `file:line` |
| **Architecture Decision Records** (`docs/adr/*`) | Vietnamese or English | Vietnamese for internal project ADRs; technical terms in English |
| **Game localization & Player texts** (`resources/locales/*`) | Vietnamese & English | Structured localization keys (`text_vi.json`, `text_en.json`) |

## Architecture Decision Records (ADR)

An ADR is **mandatory** for any architectural shift, new dependency, wire protocol format change, or database addition.

### ADR Lifecycle & Statuses
- `Proposed`: Initial draft created by engineer or agent, awaiting technical lead review.
- `Accepted`: Approved decision; actively guides implementation.
- `Superseded by ADR-N`: Replaced by a newer decision (must include backlink to new ADR).

### Required Structure for a New ADR
Located in `docs/adr/ADR-NNN-<kebab-case-title>.md`:
1. **Title**: `ADR-NNN: <Short Title>`
2. **Status**: `Proposed` | `Accepted` | `Superseded by ADR-XXX`
3. **Context**: The technical problem, constraints, and current state. Cite existing `file:line` where applicable.
4. **Decision**: Numbered points (`D1`, `D2`, ..., `Dn`) detailing exact technical choices.
5. **Alternatives Considered**: Other approaches explored and why they were rejected.
6. **Consequences**: Positive effects, trade-offs, and technical debt incurred.

## Keeping Agent Documentation Lean

- **Single Source of Truth**: State a rule once in its owning skill. Other docs should link to it rather than duplicating.
- **Current Facts Only**: Avoid changelogs or historical narrative in agent-facing skills.
- **Reference by Path**: Use relative repo paths (`engine/Systems/MovementSystem.cs:45`) instead of pasting large code blocks.
- **No Placeholder Docs**: Do not check in empty files or headers without substance.

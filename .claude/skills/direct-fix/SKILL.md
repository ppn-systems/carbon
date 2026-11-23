---
name: direct-fix
description: Use when the owner explicitly asks to fix something directly, end to end, without authoring a numbered prompt ("sửa luôn", "sửa nhanh", "làm luôn rồi mở PR", "fix it directly") — surgical triage, root-cause investigation, scoped verification, branch creation, and PR workflow.
---

# Direct Fix (No Numbered Prompt)

Use this procedure when instructed to fix a problem or implement a small task directly without going through the prompt authoring loop (`prompt-author` / `prompt-execute`).

## Routing Decision

| Situation | Action |
| :--- | :--- |
| **Small, low-risk, clear root cause** | Proceed with this direct-fix procedure |
| **Large, risky, architectural or wire protocol breaking change** | Stop and advise the user; propose authoring an ADR or a formal prompt (`prompt-author`) |
| **User specified a prompt number** | Use `prompt-execute` instead |

## Execution Steps

1. **Investigate Root Cause (T1)**:
   - Locate the exact code: search and inspect the offending files in `contracts/`, `engine/`, `server/`, etc.
   - Confirm root cause before touching any code.
2. **Isolate on Feature Branch (T2)**:
   - Branch from `origin/master`: `git checkout -b fix/<short-slug>` or `feat/<short-slug>`.
   - Never commit directly to `master`.
3. **Surgical Implementation (T3)**:
   - Modify only what is strictly required to solve the issue (Karpathy Guidelines).
   - Comply with `csharp-style`, `performance-aot`, and `do-not-do`.
4. **Scoped Verification (T4)**:
   - Build touched projects with zero warnings (`TreatWarningsAsErrors`).
   - Run covering unit/integration tests to ensure no regressions.
5. **Commit & Open PR (T5)**:
   - Commit following Conventional Commits: `<type>(<scope>): <subject>`.
   - Push and open a Pull Request against `master`.

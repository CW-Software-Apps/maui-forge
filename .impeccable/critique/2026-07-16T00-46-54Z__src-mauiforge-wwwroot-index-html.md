---
target: src/MauiForge/wwwroot/index.html
total_score: 25
p0_count: 2
p1_count: 3
timestamp: 2026-07-16T00-46-54Z
slug: src-mauiforge-wwwroot-index-html
---
Method: dual-agent (A: ses_0979de03bffeSMNJXM7bu6JSIa · B: ses_0979dc6a8ffe9IwbJaht0pEerS)

## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 3/4 | Real-time logs & scan indicator good; no build progress bar, no skeleton loading |
| 2 | Match System / Real World | 4/4 | Dev terminology, semver format, monospace paths — fluent in the domain |
| 3 | User Control and Freedom | 2/4 | No Escape on modals, no Undo for version changes, hover-only path remove |
| 4 | Consistency and Standards | 3/4 | Component patterns consistent; alert() dialogs break the design language |
| 5 | Error Prevention | 1/4 | Bump & Push has zero confirmation — highest-stakes action, no guardrails |
| 6 | Recognition Rather Than Recall | 4/4 | All state visible at once — versions, git, branch, tech type. No drill-down needed |
| 7 | Flexibility and Efficiency | 2/4 | Zero keyboard shortcuts. No bulk ops. IDE dropdown is hover-only |
| 8 | Aesthetic and Minimalist Design | 3/4 | Clean palette, good density. Gradient logo + 4 simultaneous pulsing animations = noise |
| 9 | Error Recovery | 2/4 | alert() for errors (jarring), no undo for writes, empty states don't teach |
| 10 | Help and Documentation | 1/4 | No help button, no tooltips, no inline guidance. GitHub link is the only doc path |
| **Total** | | **25/40** | **Acceptable — significant improvements needed** |

## Anti-Patterns Verdict

**LLM assessment:** Borderline. Avoids most AI tells but the gradient logo is a clear AI signature. Missing skeleton states, alert() dialogs, and hover-only IDE dropdown feel like unfinished decisions rather than designed choices.

**Deterministic scan:** 3 findings — 2 overused-font warnings for Inter (designed choice, accepted) + 1 gray-on-color false positive (near-black on amber matches spec).

## What's Working

1. State density on one screen — versions, git, branch, tech type all visible at once.
2. SignalR real-time logging with smart auto-scroll and color parsing.
3. OKLCH palette implementation matching DESIGN.md with high token fidelity.

## Priority Issues

**[P0] No confirmation on Bump & Push** — pushes to remote git with zero dialog.
**[P0] No keyboard shortcuts** — zero accelerators in a dev tool.
**[P1] Error feedback uses alert()** — system dialog jarring, unstylable.
**[P1] No skeleton loading states** — text spinner weakest first impression.
**[P1] Missing prefers-reduced-motion** — 4+ animated elements, no fallback.
**[P2] Sidebar never collapses at <1024px** — always 320px.
**[P2] IDE dropdown hover-only + clips at top** — touch-inaccessible.
**[P2] Gradient logo** — absolute ban violation.
**[P2] No ARIA on modals + no focus trap** — screen readers and keyboard users impacted.
**[P3] Design spec deviations** — missing --purple, card shadows, accent borders, filled badge variants.

## Persona Red Flags

**Alex (Power User):** Zero keyboard shortcuts. No bulk ops. IDE dropdown closes on 1px mouse stray. Filters not persisted on reload.

**Sam (Accessibility):** No prefers-reduced-motion. No focus trap on modals. No ARIA on either modal. No :focus-visible anywhere. IDE dropdown hover-only.

## Minor Observations

- border-slate-855 doesn't exist in Tailwind — renders as no-op
- No label for select elements
- Version input type="text" instead of inputmode="decimal"
- Terminal "Clear" appends a line instead of actually clearing
- SignalR connection failure silently swallowed
- Three different empty-state styles
- Version badge v--- flash on load

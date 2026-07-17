---
target: src/MauiForge/wwwroot/index.html (web dashboard)
total_score: 29
p0_count: 0
p1_count: 2
timestamp: 2026-07-17T11-47-09Z
slug: src-mauiforge-wwwroot-index-html
---
Method: dual-agent (A: a3935046bcd74f0e5 · B: a0578c320d0f1af14)

## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 3 | "Local Host Connected" is static text, never reflects actual SignalR connection state |
| 2 | Match System / Real World | 4 | Git/version jargon matches dev mental model precisely |
| 3 | User Control and Freedom | 3 | Escape closes 3 of 4 modals but not the generic confirmModal |
| 4 | Consistency and Standards | 2 | List view still uses old P/A/i letter monograms + fixed 3-row layout while Card view got the adaptive fix |
| 5 | Error Prevention | 3 | Bump & Push has a real diff-preview confirm; Save Local has no equivalent friction (low risk, acceptable) |
| 6 | Recognition Rather Than Recall | 4 | Extensive icon+label+tooltip coverage |
| 7 | Flexibility and Efficiency | 3 | Quick-bump shortcuts, Ctrl+F, persisted layout/sidebar state; no bulk actions across apps |
| 8 | Aesthetic and Minimalist Design | 3 | MAUI card action row hits 5 buttons — busy for a "no ceremony" tool |
| 9 | Error Recovery | 2 | loadPaths/openFolder/addPath catch blocks only console.error, no toast |
| 10 | Help and Documentation | 2 | Tooltips are the only help; no shortcut list, no empty-state onboarding |
| **Total** | | **29/40** | **Good — address weak areas, solid foundation** |

## Anti-Patterns Verdict

Does not read as AI-generated: bespoke OKLCH token ladder, deterministic per-app avatar palette, per-project-type visual identity, and a fixed-position global row menu that genuinely fixes a clipping bug rather than papering over it with z-index.

Deterministic scan: 4 findings — 2x overused-font (Inter, lines 53/159), 2x layout-transition (lines 175/186, sidebar/terminal animating width/height). The overused-font hits are false positives (PRODUCT.md register is "product", where Inter is explicitly permitted). The layout-transition hits are real but low-severity (manual-toggle transitions, not continuous).

Visual overlays: not available — no browser automation tool exposed this session. Confirmed the server is running (curl returned 200 on localhost:5123) but no browser tool to inject the overlay.

## Overall Impression

The foundation is solid and clearly authored, not generic. The biggest issue: the most recent fix (adaptive version rendering per project type) only landed in Card view, not List view — switching to "List" resurfaces the exact cryptic P/A/i pattern that was already fixed elsewhere. A real consistency regression, not cosmetic.

## What's Working

- versionPanelHtml renders version info per actual project shape instead of faking "N/A" for platforms that don't exist.
- openRowMenu — viewport-clamped position:fixed menu genuinely fixes the clipping bug.
- Light/dark OKLCH token system gives every zone (bg/chrome/surface/table-head/hover) a distinct, intentional tonal step.

## Priority Issues

**[P1] List view fix regressed.** Table rendering (~line 1532) still calls platformChip("P") directly with the fixed 3-row layout and raw monograms — the exact cryptic pattern already fixed in Card view.
**Why it matters**: inconsistency between the two view modes of the same screen; switching to "List" loses clarity just rebuilt in Card view.
**Fix**: extract one shared adaptive version-render function used by both Card and List.
**Suggested command**: /impeccable distill

**[P1] Action-row overload.** MAUI cards show 5 buttons (Bump+1, Build+1, Android, iOS, overflow) plus a 6-item overflow menu — past the ~4-choice cognitive load guideline, and contradicts the "no ceremony" brand promise.
**Fix**: collapse Android/iOS builds into one "Build" control with a platform sub-choice.
**Suggested command**: /impeccable distill

**[P2] Silent failures.** loadPaths/openFolder/addPath catch blocks only console.error, inconsistent with pullGit/bump-push which always surface a toast.
**Fix**: standardize a fetch-catch-toast wrapper.
**Suggested command**: /impeccable harden

**[P2] Keyboard/focus inconsistency.** confirmModal isn't in the Escape-key handler; no modal actually traps Tab (only sets initial focus) — keyboard users can Tab behind an open overlay.
**Fix**: add confirmModal to Escape handling; implement real focus-cycling.
**Suggested command**: /impeccable harden

**[P3] Loose details.** relativeTime() hardcodes Portuguese strings ("agora", "3d atrás") in an otherwise English UI; version badge shows raw "v---" until the API resolves; col-span-2 on "Behind" cards is dead code on single-column grids at narrow widths.
**Suggested command**: /impeccable polish

## Persona Red Flags

**Alex (power user)**: no bulk bump/push across multiple apps — every version change is one-app-at-a-time despite the dashboard existing to manage many.
**Sam (accessibility-dependent)**: globalRowMenu buttons have no role="menu"/role="menuitem" or arrow-key nav; since no modal truly traps focus, a keyboard user can Tab out behind an open overlay.

## Minor Observations

- relativeTime() speaks Portuguese in an English UI — deliberate or leftover?
- Raw "v---" flashes before loadVersionInfo() resolves.
- col-span-2 for "Behind" cards is dead code on single-column grids.

## Questions to Consider

1. If the brand promise is "surgical instrument, no ceremony," does a 5-button row plus a 6-item overflow menu still qualify, or has feature completeness quietly become ceremony?
2. relativeTime() speaks Portuguese in an English interface — deliberate bilingual choice, or will it confuse anyone else running this dashboard?

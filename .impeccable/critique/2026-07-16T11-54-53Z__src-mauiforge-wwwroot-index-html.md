---
target: src/MauiForge/wwwroot/index.html (dashboard list/card layout)
total_score: 20
p0_count: 2
p1_count: 2
timestamp: 2026-07-16T11-54-53Z
slug: src-mauiforge-wwwroot-index-html
---
Method: dual-agent (A: acf703602a947ab5b · B: a4002e40efdb7c07a)

## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 3 | SignalR log stream + scan pill + spinning refresh are solid |
| 2 | Match System / Real World | 3 | Version/build/git terms are domain-correct for devs |
| 3 | User Control and Freedom | 2 | No undo on Bump+Push beyond the confirm modal; no bulk actions |
| 4 | Consistency and Standards | 2 | Card view uses Git badge pills, list view uses plain colored text for the same data |
| 5 | Error Prevention | 3 | Bump/Push has a diff-preview confirm modal, but "Bump +1" skips it entirely |
| 6 | Recognition Rather Than Recall | 1 | VERSIONS column: 3 unlabeled monospace lines distinguished only by a 10px gray prefix |
| 7 | Flexibility and Efficiency | 2 | Search + 2 filters exist; no column sort, no keyboard row actions |
| 8 | Aesthetic and Minimalist Design | 1 | Near-uniform near-black across header/sidebar/table; 7 same-weight buttons per row |
| 9 | Error Recovery | 2 | Toasts exist for API errors; row-level failures have no inline error state |
| 10 | Help and Documentation | 1 | No tooltips beyond a couple `title=` attrs, no empty-state guidance beyond one line |
| **Total** | | **20/40** | **Acceptable — significant improvements needed** |

## Anti-Patterns Verdict

**Start here.** Does this look AI-generated? **Yes, but a competent version of it.**

**LLM assessment**: The badge/pill/card vocabulary is textbook Tailwind-dashboard boilerplate. Icon fallback is a bare "MF" text box repeated per row instead of a designed glyph system. The 7-button flat action row (Version / Bump +1 / Pull / Folder / IDE / Android / iOS) with no primary action chosen is the single most diagnostic tell — every option got a button instead of a decision being made about what matters most. The color system itself is real (OKLCH tokens, semantic vars) but never gets applied with intent in the list view: badge classes exist (`.badge-green`/`.badge-amber`/`.badge-red`) but the Git column ignores them and falls back to raw inline text color. That gap between a real design system and its actual usage is a classic sign of iteratively-patched AI-authored HTML.

**Deterministic scan**: 3 raw hits — 2× `overused-font` (Inter, lines 41/103), 1× `gray-on-color` (line 1011). The `gray-on-color` hit is a **false positive**: `text-slate-950` (~#020617, near-black) on `bg-amber-500` is high-contrast, not washed-out gray text — the rule's heuristic matches any `text-slate-*` on a `bg-*-500` without checking how dark the "slate" actually is. The `overused-font` hits are real (Inter is genuinely loaded) but not automatically a defect: this project's PRODUCT.md register is "product" (dashboard/tool), where Inter/system-sans is an explicitly permitted default — not the AI-slop tell it would be on a marketing/brand surface. Worth a deliberate look, not an automatic swap.

**Visual overlays**: Not available — no browser automation tool exposed in this environment. No live overlay was shown; this critique relies on the design-review pass reading the actual source plus the user's screenshot description.

## Overall Impression

The design *system* underneath this dashboard is more mature than what's actually rendered. Real OKLCH tokens, a real badge component, a real confirm-modal pattern for destructive actions — but the list/table view was assembled under time pressure and bypasses half of that system (plain text instead of badges, no primary/secondary button hierarchy, near-identical surface tones). The user's "tudo muito preto" and "cards e lista não tão bons" are both accurate: the tonal ladder is too shallow to read as distinct zones, and the row actions are cognitively overloaded. The single biggest opportunity is collapsing the 7-button row into one clear primary action + an overflow menu, paired with a genuinely stepped surface hierarchy (not just a slightly-less-black table row).

## What's Working

- **Token architecture is real**: OKLCH custom properties for bg/surface/accent/status colors form a proper design-system foundation, not ad hoc hex values.
- **Accessibility instincts exist**: focus-trap on modals, `prefers-reduced-motion` handling, Escape-to-close, Ctrl/Cmd+F to search — above-average for this register even where execution is incomplete.
- **Bump & Push confirmation flow** shows real thought about a destructive, hard-to-undo action: amber warning banner, before/after diff preview, red confirm button.

## Priority Issues

**[P0] Action-row overload (list & card).** 7 undifferentiated ghost buttons per row (Version, Bump +1, Pull, Folder, IDE, Android, iOS), all identical size/weight/color, no primary/secondary distinction — `.btn-primary` exists in CSS but is never used in row actions. With 15 rows that's 100+ identically-styled interactive targets on screen at once, blowing past the ~4-item working-memory limit for a repeated control.
**Why it matters**: users must re-scan all 7 buttons on every row to find the one they want; the actually-important action (Bump & Push) is buried among low-frequency utility actions (Folder, IDE).
**Fix**: promote one primary action per row (Bump & Push, styled `.btn-primary`) + collapse the rest into the overflow "⋯"/IDE-dropdown pattern already built for IDE selection.
**Suggested command**: /impeccable layout

**[P0] Tonal flatness across surfaces.** `--bg` (L .055) → `--surface` (L .09) → header `bg-slate-900/50` are all within ~0.03–0.05 OKLCH lightness of each other. Header, sidebar, table, and rows read as one undifferentiated near-black field.
**Why it matters**: this is exactly the "tudo muito preto" complaint — no zone reads as structurally distinct, so the eye has nothing to anchor hierarchy on.
**Fix**: widen the elevation ladder (e.g. bg .04 / sidebar .07 / table-head .11 / row .09 / row-hover .14) and give the table header a genuinely distinct fill instead of a translucent overlay on the same near-black.
**Suggested command**: /impeccable layout

**[P1] Git status has zero affordance in list view.** Badge classes (`.badge-green`/`.badge-amber`/`.badge-red`) are defined and correctly used in card view, but list view falls back to plain inline-colored text for the identical data.
**Why it matters**: inconsistency (heuristic #4) — same concept, two different visual treatments depending on which layout mode is active; plain text is also slower to scan than a colored chip.
**Fix**: reuse the existing `.badge` component in the table cell.
**Suggested command**: /impeccable layout

**[P1] VERSIONS column has no label hierarchy.** Three monospace lines (Project/Android/iOS) differ only by a 10px muted text prefix.
**Why it matters**: forces recall instead of recognition (heuristic #6) — at a glance in a dense table, the platform each version number belongs to isn't scannable.
**Fix**: fixed-width platform chips/icons (e.g. small "P"/"A"/"i" mono-letter badges) with aligned columns, or a compact mini-table instead of stacked lines.
**Suggested command**: /impeccable layout

**[P2] Two badge systems coexist.** `.badge` is `font-size:0.625rem`/`padding:0.125rem 0.5rem`, but the header version pill uses raw Tailwind `text-[10px] px-2 py-0.5` instead.
**Why it matters**: minor consistency drift; low visual impact but easy to fix while touching badges anyway.
**Fix**: consolidate on the `.badge` class everywhere.
**Suggested command**: /impeccable layout

## Persona Red Flags

**Alex (Power User)**: Wants one-click Bump+Push per row on 5–10 apps a day. Currently must locate the right button among 7 identical ghost buttons per row — the row itself never visually promotes "Bump & Push" as the primary action, costing scan time across 15 rows even though the action itself (once found) is genuinely fast.

**Sam (Accessibility-Dependent User)**: `:focus-visible` outline exists — good. But ghost buttons at 11px font with equal-weight styling have no `aria-label` on the icon-only refresh button (`title=` only, not announced consistently by all screen readers), and the IDE dropdown has no `aria-expanded`/`aria-haspopup`, so its open/closed state is invisible to assistive tech.

## Minor Observations

- Table row height/padding isn't clearly defined beyond button padding — risks cramped click targets at 11px button text.
- Icon fallback ("MF" text box) repeats identically for every app without a real icon; a deterministic color-from-name avatar would read better at a glance than a static gray box.
- `overused-font` (Inter) is permitted by this project's own "product" register — not an automatic fix, just worth a deliberate yes/no.

## Questions to Consider

- If every row action were forced to a hard limit of 3 visible buttons, which 3 would survive — and does that answer confirm Bump & Push should be the row's `.btn-primary`?
- The badge system in the CSS is more mature than its usage in the list view — was it built and then abandoned mid-implementation?
- Is "near-black + one accent, restrained" in DESIGN.md license to skip a tonal ladder entirely, or was multi-tier surface elevation part of the original intent and just never built?

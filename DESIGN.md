# MAUI Forge — Design System

## Theme direction

**Electric terminal.** True near-black, no blue-tinted neutrals — the base is a real void, not a "dark SaaS" tint. Primary actions and current-selection state are carried by a single committed accent: a vivid electric cyan, high-voltage against the near-black surface. Blue is demoted to a secondary/technical color (info toasts, WPF category badge) — never the primary action color. Amber/gold, green, and red stay conventional for their semantic roles (warning, success, error) since git/build status benefits from familiar meaning over novelty.

Color strategy: **Restrained with a committed accent.** The surface stays pure neutral (chroma 0). Electric cyan carries primary actions, selection, and focus states exclusively. Amber/gold carries warnings and dirty/pending git status. Green carries success. Red carries errors. Color is a communication layer, never a decoration layer.

## Color Palette (OKLCH)

### Base

```css
--bg:             oklch(0.055 0 0);        /* true near-black — no hue tint */
--surface:        oklch(0.09 0 0);         /* cards, panels, sidebars */
--surface-hover:  oklch(0.13 0 0);         /* hover states, elevated elements */
--surface-raised: oklch(0.17 0 0);         /* modals, dropdowns, focus rings */
--border:         oklch(0.22 0 0);         /* subtle separators */
```

### Text

```css
--ink:            oklch(0.95 0 0);         /* body text — near-white, ≥10:1 vs bg */
--ink-dim:        oklch(0.68 0 0);         /* secondary labels, metadata */
--ink-muted:      oklch(0.48 0 0);         /* placeholders, disabled (≥3.5:1 vs surface) */
--ink-inverse:    oklch(0.07 0 0);         /* text on saturated fills */
```

### Brand & Action

```css
--accent:         oklch(0.78 0.15 195);    /* primary — electric cyan */
--accent-hover:   oklch(0.72 0.16 195);    /* darker hover */
--accent-active:  oklch(0.66 0.17 195);    /* pressed state */
--accent-subtle:  oklch(0.22 0.09 195);    /* ghost/outline backgrounds */
```

### Secondary / technical

```css
--blue:           oklch(0.58 0.14 250);    /* secondary only — info toast, WPF badge. Never primary actions. */
--blue-hover:     oklch(0.52 0.15 250);
--blue-active:    oklch(0.46 0.15 250);
--blue-subtle:    oklch(0.22 0.07 250);
```

### Semantic

```css
--amber:          oklch(0.83 0.17 85);     /* warnings, dirty git status, attention */
--amber-subtle:   oklch(0.24 0.08 85);     /* badge bg */
--green:          oklch(0.79 0.17 150);    /* success, clean, synced */
--green-subtle:   oklch(0.20 0.07 150);    /* badge bg */
--red:            oklch(0.70 0.19 22);     /* error, failure, behind */
--red-subtle:     oklch(0.22 0.09 22);     /* badge bg */
--purple:         oklch(0.58 0.14 320);    /* non-main branches, secondary markers */
--purple-subtle:  oklch(0.22 0.08 320);    /* badge bg */
--indigo-maui:    oklch(0.60 0.16 295);    /* MAUI tech badge */
--indigo-maui-subtle: oklch(0.22 0.08 295);/* MAUI badge bg */
```

### Terminal (Spectre.Console) mapping

The terminal still ships the prior royal-blue accent — bringing it in line with the web dashboard's electric-cyan accent is a follow-up, not yet applied to `AppListScreen.cs` / `AppDetailScreen.cs`:

| Role | Web (OKLCH) | Spectre.Console |
|------|-------------|-----------------|
| Primary action | `--accent` (web) / `--blue` (terminal, pending) | `royalblue1` (#4876FF) |
| iOS label | `--blue` lighter | `skyblue1` |
| Android label | `--green` | `green3` |
| Warning | `--amber` | `yellow` / `gold1` |
| Error | `--red` | `red` |
| Success | `--green` | `green` |
| Muted text | `--ink-muted` | `grey46` / `grey53` |
| Ink | `--ink` | `white` / `default` |
| Separators | `--border` | `grey23` |
| Accent (branches) | `--purple` | `fuchsia` |

---

## Typography

### Web dashboard

- **UI**: Inter (300, 400, 500, 600, 700) — clean, legible, neutral
- **Code**: JetBrains Mono (400, 500) — terminal output, versions, file paths
- **Scale**: 12 / 13 / 14 / 15 / 18 / 22 / 28 px — tight product scale, no fluid sizing
- **Body max-width**: 75ch on prose sections; data tables unrestricted
- **Headings**: `text-wrap: balance` on h1–h3

### Terminal UI

- Uses terminal default monospace font for all text
- Spectre.Console handles all typographic rendering
- Decorative font distinctions (Figlet header) kept minimal — one figlet word at launch

---

## Components

### Buttons

**Primary fill** (`--accent` bg, `--ink-inverse` text)
- Border-radius: 8px (web) / rounded by Spectre
- Hover: `--accent-hover`
- Active: `--accent-active`
- Disabled: 40% opacity
- Font: 600, 13px (web)

**Ghost / outline** (transparent bg, `--accent` text, `--accent-subtle` border)
- Hover: `--surface-hover` bg

**Danger** (`--red` bg or outline)
- Same shape as primary — never a different style

### Status badges

- Filled pill shape (web: rounded-full, 10px font)
- Text is `--ink-inverse` (white) on saturated fills
- Semantic colors: `--green` for clean/synced, `--amber` for dirty/warning, `--red` for behind/error
- Subtle variants: `--*-subtle` bg + full saturation text (for less emphasis)

### Cards (web)

- Background: `--surface`
- Border: `--border`, uniform across categories — the tech badge (MAUI/Blazor/WPF/Unity/ClassLib) is the sole category signal. (A per-type colored left-border accent was tried and dropped: at 1px it was a near-invisible side-stripe that duplicated the badge without adding legibility — a banned pattern in this design system regardless.)
- Border-radius: 12px
- Padding: 20px
- Shadow: minimal (`box-shadow: 0 1px 3px rgba(0,0,0,0.3)`)
- Hover: `--surface-hover` background + slightly brighter border

### Tables

- Header: 11px uppercase tracking, `--ink-muted` text
- Row background: transparent; hover: `--surface-hover`
- Borders: `--border` horizontal dividers only
- Font: 12px / 13px, monospace for version/code columns

### Modals (web)

- Backdrop: `rgba(0,0,0,0.6)` with blur
- Panel: `--surface-raised` bg, `--border` border
- Rounded: 16px top-level container
- Close button: top-right, `--ink-muted`, hover `--ink`

### Form controls (web)

- Input bg: `--bg` (near-black), border: `--border`
- Focus: `--accent` ring (2px offset)
- Placeholder: `--ink-muted`
- Select/option: consistent with input styling
- Text: 13px, `--ink`

---

## Layout

### Web dashboard

- Fixed header (60px) + sidebar (320px) + main content area
- Bottom terminal panel: collapsible, 256px default height
- Sidebar: path list + add-folder form
- Main: search/filter bar + app list (card grid or table, toggleable)
- Responsive breakpoints: sidebar collapses at <1024px into a top-menu drawer

### Terminal UI

- Full terminal width, no fixed constraints
- Detail panel rendered inside Spectre Panel with border
- Git and config info stacked vertically for scan-readability
- Actions grouped by category with section headings

---

## Motion

### Web dashboard

- Transitions: 150ms ease-out for hovers, 200ms for panel open/close
- Layout toggle (card ↔ list): instant (no animation — tool context)
- Modals: 200ms fade + 50ms scale entrance
- Build output streaming: instant append — no per-line animation
- Reduced motion: respect `prefers-reduced-motion` — crossfade only

### Terminal UI

- No motion. Terminal doesn't animate. Status spinners use Spectre's built-in.

---

## Spacing

### Web dashboard

- 4px grid unit
- Panel padding: 24px (x-axis), 20px (y-axis)
- Card grid gap: 24px
- Content max-width: 1400px (dashboard), unlimited (detail views)

### Terminal UI

- Spectre.Console default padding (1 space)
- Sections separated by blank lines and Rule elements
- No custom spacing — defer to terminal rendering

---

## Icons

- Web: emoji-based (current approach is adequate for a dev dashboard; no icon library needed)
- Terminal: none / Spectre uses markup symbols and text

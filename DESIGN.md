# MAUI Forge — Design System

## Theme direction

**Midnight control center.** Deep navy authority, precise blue beams, warm amber signals. The tool lives in the terminal — the web dashboard is its visual extension, not a separate product. Both surfaces share one design language: dark, confident, information-dense, with color reserved for signal, not decoration.

Color strategy: **Restrained with a committed accent.** The surface stays pure dark neutral. Blue carries primary actions, selection, and navigation highlights. Amber/gold carries warnings and build status. Green carries success. Color is a communication layer, never a decoration layer.

## Color Palette (OKLCH)

### Base

```css
--bg:             oklch(0.06 0.000 0);     /* near-black — terminal-like void */
--surface:        oklch(0.10 0.005 260);   /* cards, panels, sidebars */
--surface-hover:  oklch(0.14 0.008 260);   /* hover states, elevated elements */
--surface-raised:  oklch(0.18 0.010 260);  /* modals, dropdowns, focus rings */
--border:         oklch(0.20 0.012 260);   /* subtle separators */
```

### Text

```css
--ink:            oklch(0.93 0.008 260);   /* body text — cool white, ≥10:1 vs bg */
--ink-dim:        oklch(0.65 0.015 260);   /* secondary labels, metadata */
--ink-muted:      oklch(0.42 0.012 260);   /* placeholders, disabled (≥3.5:1 vs surface) */
--ink-inverse:    oklch(0.08 0.000 0);     /* text on saturated fills */
```

### Brand & Action

```css
--blue:           oklch(0.48 0.20 260);    /* primary — confident royal blue */
--blue-hover:     oklch(0.44 0.22 260);    /* darker hover */
--blue-active:    oklch(0.40 0.22 260);    /* pressed state */
--blue-subtle:    oklch(0.20 0.08 260);    /* ghost/outline backgrounds */
```

### Semantic

```css
--amber:          oklch(0.65 0.16 85);     /* warnings, build running, attention */
--amber-subtle:   oklch(0.22 0.06 85);     /* badge bg */
--green:          oklch(0.55 0.16 145);    /* success, clean, synced */
--green-subtle:   oklch(0.18 0.06 145);    /* badge bg */
--red:            oklch(0.50 0.20 25);     /* error, failure, behind */
--red-subtle:     oklch(0.20 0.08 25);     /* badge bg */
--purple:         oklch(0.50 0.15 290);    /* non-main branches, secondary markers */
--purple-subtle:  oklch(0.20 0.08 290);    /* badge bg */
--indigo-maui:    oklch(0.48 0.18 280);    /* MAUI project card accent */
--indigo-maui-subtle: oklch(0.20 0.08 280);/* MAUI badge bg */
```

### Terminal (Spectre.Console) mapping

The terminal palette mirrors the web dashboard with Spectre-compatible colors:

| Role | Web (OKLCH) | Spectre.Console |
|------|-------------|-----------------|
| Primary action | `--blue` | `royalblue1` (#4876FF) |
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

**Primary fill** (`--blue` bg, `--ink-inverse` text)
- Border-radius: 8px (web) / rounded by Spectre
- Hover: `--blue-hover`
- Active: `--blue-active`
- Disabled: 40% opacity
- Font: 600, 13px (web)

**Ghost / outline** (transparent bg, `--blue` text, `--blue-subtle` border)
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
- Border: `--border` with per-type accent border (`--indigo-maui` for MAUI, `--amber` for Blazor, `--blue` for WPF, `--ink-muted` for Unity, `--border` for ClassLib)
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
- Focus: `--blue` ring (2px offset)
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

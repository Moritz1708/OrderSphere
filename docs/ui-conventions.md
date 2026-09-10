# OrderSphere — UI & Styling Guide

Binding reference for all visual, theming, MudBlazor, and CSS work in `src/Frontend/OrderSphere.Web`.

The design direction is **"Bold Editorial"**: magazine typography, high contrast, one vivid accent,
hairline rules instead of shadows, asymmetric bento layouts, and motion that is present but never
decorative for its own sake. It replaces the previous "Flat & Focused" multi-brand system — see
[ADR 0013](adr/0013-editorial-design-system.md) for why.

> **Status: the redesign is in progress.** Sections 1–5 are final and binding. Section 6 grows as the
> `Os*` component kit lands. `wwwroot/css/legacy.css` is a transitional file holding the classes the
> not-yet-redesigned pages still use; it is deleted once the kit migration completes. Do not add to it.

The canonical implementations are:

- Palette and layout constants: `src/Frontend/OrderSphere.Web/Services/DesignTokens.cs`
- Design tokens: `src/Frontend/OrderSphere.Web/wwwroot/css/tokens.css`
- MudBlazor theme: `src/Frontend/OrderSphere.Web/Services/ThemeState.cs`

Change those first; this guide documents what they contain.

---

## 1. Design principles

- **One light theme, one dark theme.** No brands, no per-tenant palettes. A design may assume the
  exact palette below.
- **Hairlines, not elevation.** Every MudBlazor elevation step is `"none"`. Separation comes from
  1px `--os-hairline` rules and flat surface changes. There is exactly one shadow,
  `--os-shadow-soft`, and it appears on hover only.
- **Serif display, sans body, mono numerals.** Three type roles, no exceptions (§3).
- **Tokens, never literals.** Use `var(--os-*)` in components. `var(--mud-palette-*)` is owned by
  `MudThemeProvider`; do not reference it from component markup or `components.css`.
- **Motion respects the reader.** Every animation runs on a duration token and disappears entirely
  under `prefers-reduced-motion` (§5).

---

## 2. Tokens (`wwwroot/css/tokens.css`)

### Colour

| Token | Light | Dark | Use |
|---|---|---|---|
| `--os-canvas` | `#F7F5F0` | `#0F0E0C` | Page background |
| `--os-paper` | `#FFFFFF` | `#171613` | Raised surface: cards, drawers, dialogs, table head |
| `--os-sunk` | `#EFECE5` | `#0A0908` | Recessed surface: alternating sections, hover rows |
| `--os-ink` | `#14120F` | `#F2EFE9` | Primary text |
| `--os-muted` | `#6B6560` | `#A39D95` | Secondary text and captions |
| `--os-accent` | `#FF4D1F` | `#FF6A3D` | **Fills, display text ≥24px and borders only** |
| `--os-accent-ink` | `#C93A0F` | `#FF6A3D` | Accent for text below 24px |
| `--os-on-accent` | `#14120F` | `#0F0E0C` | Text on an accent fill. Never white |
| `--os-accent-2` | `#1F4DFF` | `#6B8CFF` | Links, focus rings |
| `--os-success` / `--os-warning` / `--os-error` | see `tokens.css` | | Status text and icons |
| `--os-warning-fill` | `#E8A317` | `#FFB84D` | Warning as a fill; its text is `--os-ink` |
| `--os-hairline` | ink 12% | ink 12% | Decorative rules and card borders |
| `--os-hairline-strong` | ink 24% | ink 24% | Emphasised rules, table head underline |
| `--os-border-control` | ink 48% | ink 48% | **Interactive outlines** — inputs, steppers, outlined buttons |
| `--os-block` / `--os-on-block` | ink / canvas | paper / ink | A deliberately dark panel |

Two rules carry real weight:

- **`--os-accent` is 3.04:1 on canvas in light mode.** It is legible at display sizes and as a fill or
  border, not as body text. Use `--os-accent-ink` for anything below 24px.
- **`--os-border-control`, not `--os-hairline-strong`, outlines interactive controls.** WCAG 1.4.11
  requires 3:1 for a control's visible boundary, which the decorative hairline does not reach.

`DesignTokensContrastTests` asserts every ratio above; `TokensCssSyncTests` asserts that `tokens.css`
and `DesignTokens.cs` hold the same values. Retuning a colour means updating both and letting those
tests confirm the result is still usable.

Tinted fills for chips and alerts are derived, never hand-mixed:
`--os-accent-bg`, `--os-info-bg`, `--os-success-bg`, `--os-warning-bg`, `--os-error-bg`, `--os-neutral-bg`.

### Scale

| Group | Tokens |
|---|---|
| Type | `--os-text-d1/d2/d3` (fluid `clamp()` display sizes), `--os-text-xs/sm/base/lg/xl` |
| Line height | `--os-lh-tight` `.95`, `--os-lh-snug` `1.15`, `--os-lh-body` `1.6`, `--os-lh-relaxed` `1.7` |
| Space | `--os-space-1..10` → 4/8/12/16/24/32/48/64/96/128px |
| Radius | `--os-radius-control` 6px, `--os-radius-card` 16px, `--os-radius-popover` 8px, `--os-radius-pill` |
| Motion | `--os-dur-1..4` → 120/180/220/320ms, `--os-ease`, `--os-stagger` 60ms |
| Layout | `--os-header-h` 64px, `--os-container` 1320px, `--os-container-narrow` 800px, `--os-gutter` |
| Depth | `--os-shadow-soft` — the only shadow, hover only |

---

## 3. Typography

| Role | Family | Where |
|---|---|---|
| Display | Instrument Serif, regular + italic | `h1`–`h4`, page titles, pull quotes, dialog titles |
| Body / UI | Manrope 400/600/700/800 | Everything else. Weight 500 is not shipped |
| Numeric / metadata | Geist Mono (variable) | Prices, SKUs, order numbers, timestamps, eyebrows, table heads, counts |

Headings never uppercase and never bold-face the serif — weight 400 with tight leading is the look.
One `<em>` inside a display heading renders italic in `--os-accent-ink`; use it on at most one word.
Eyebrows are mono, 0.72rem, uppercase, `0.12em` tracking, with a leading 24px rule.

Fonts are self-hosted in `wwwroot/fonts` under SIL OFL 1.1 (licence files sit beside them). The
production CSP is `font-src 'self' data:` — **never link a font CDN.** Instrument Serif Regular and
Manrope Regular are preloaded in `index.html`; the rest load on demand.

---

## 4. CSS architecture

`wwwroot/css/app.css` declares the cascade order and pulls MudBlazor into the lowest layer:

```css
@layer mud, tokens, base, overrides, components, pages, motion, utilities;
@import url("/_content/MudBlazor/MudBlazor.min.css") layer(mud);
```

Because MudBlazor's stylesheet is layered beneath ours, **every OrderSphere rule wins on layer order
alone**. Write overrides at their natural specificity. `!important` is banned outside the
reduced-motion block in `motion.css`.

One exception to know about: MudBlazor's colour utilities (`.mud-primary-text` and friends) are
declared `!important`, and an important declaration in a lower layer still beats a normal one in a
higher layer. Do not fight them from CSS — avoid the utility at the call site: a plain `<a>` instead
of `MudLink`, `Color.Inherit` instead of `Color.Primary`.

| File | Layer | Holds |
|---|---|---|
| `tokens.css` | tokens | `:root` (light) and `:root[data-theme="dark"]` |
| `fonts.css` | tokens | `@font-face` |
| `base.css` | base | Reset, document typography, focus, scrollbar, boot screen, error bar |
| `mud-overrides.css` | overrides | MudBlazor restyling, one section per component |
| `components.css` | components | `.os-*` classes for the `Os*` kit |
| `pages.css` | pages | Page-only layout, prefixed `pg-<route>-` |
| `motion.css` | motion | Keyframes, reveal, page transition, reduced motion |
| `utilities.css` | utilities | A short list — `.os-mono`, `.os-num`, `.os-visually-hidden`, … |

There are no `.razor.css` files. Kit classes are a shared vocabulary, scoped styles cannot reach
MudBlazor internals without `::deep` on every rule, and one greppable `components.css` is reviewable.

---

## 5. Theme mechanism and motion

**Theme.** `wwwroot/js/theme-boot.js` runs synchronously in `<head>`, reads
`localStorage["os-dark-mode"]` (falling back to `prefers-color-scheme`), and stamps
`html[data-theme]` plus `html.lang` before the first paint. `Program.cs` reads the same value back
into `ThemeState` so `MudThemeProvider` emits the matching palette on its first render. The four Mud
providers live once in `App.razor`, above the router — never in a layout.

The system preference is sampled once at boot and never overrides a stored choice
(`ObserveSystemDarkModeChange="false"`). For signed-in users the choice is mirrored to
`CustomerProfile.DarkModeEnabled` best-effort, and is read back only when nothing is stored locally.

**Motion.** `wwwroot/js/motion.js` owns the `IntersectionObserver`, the theme attribute and scroll
locking; `IMotionService` is the Blazor wrapper and no-ops when the module cannot load, so tests and
script-blocked pages degrade to no animation. Scroll reveals are gated on `html[data-motion="ready"]`
so content is never hidden when JavaScript fails.

Every animation uses a duration token and `--os-ease`. `@media (prefers-reduced-motion: reduce)`
zeroes the duration tokens, neutralises all animations and transitions, and forces revealed elements
visible. Add motion only where it explains a change of state; reveal below-the-fold content only.

---

## 6. Components

The `Os*` kit in `Components/Ui` carries the visual identity; MudBlazor supplies behaviour (dialog,
snackbar, select, table, drawer, popover, date picker). Reach for a kit component before a Mud one.

| Component | Purpose |
|---|---|
| `ThemeToggle` | Header light/dark control. Writes the attribute, storage and server preference |

*(This table grows as the kit lands. Until a page is migrated it may still use the transitional
classes in `legacy.css`.)*

Cross-cutting helpers that are already binding:

- **`Services/SnackbarExtensions.cs`** — raise toasts through `ShowApiError(result.Error)`,
  `ShowSuccess`, `ShowWarning`, `ShowInfo`. Do not call `ISnackbar.Add` directly.
- **`Services/StatusPresentation.cs`** — the single mapping from a domain status string to a
  `StatusTone` and a resource key (orders, invoices, reviews, active flags, stock). Do not write a
  new `switch` over status strings.

---

## 7. Internationalization (i18n)

User-facing text is localized, not hardcoded. The supported UI languages are German (`de-DE`, the
**neutral** resource and default) and English (`en-US`), declared in `Services/SupportedCultures.cs`.
The active culture is resolved once at startup in `Program.cs` from `localStorage["os-culture"]`.

- Strings live in `Resources/AppStrings.resx` (German, neutral) and `Resources/AppStrings.en.resx`
  (English), keyed by dotted names (`Cart.Title`, `Checkout.Submit`). Marker type `AppStrings`.
- Inject `IStringLocalizer<AppStrings>` (conventionally `L`) and read `@L["Key"]`; pass arguments for
  composite strings (`@L["Cart.AriaLabel", count]`) — never concatenate translated fragments.
- Culture-dependent values go through `Services/Formatting.cs`: `Formatting.Currency`,
  `Formatting.DateTime`, `Formatting.Date`. Never `ToString("C")` or a hardcoded date pattern.

**Adding a string:** add the key to both `.resx` files — `LocalizationTests` enforces that every
neutral key has an English entry.

---

## 8. Accessibility checklist

- Every page renders exactly one `<h1>`. `App.razor` focuses it after navigation.
- Body-sized text meets 4.5:1; use `--os-accent-ink`, not `--os-accent`.
- Interactive outlines use `--os-border-control` (3:1 minimum).
- The focus ring is `--os-accent-2` at 2px with 2px offset; never remove it without a replacement.
- Icon-only controls carry a localized `aria-label`.
- Async regions announce themselves (`role="status"`, `aria-live="polite"`); skeletons are
  `aria-hidden` with a visually hidden label.
- Motion honours `prefers-reduced-motion`.

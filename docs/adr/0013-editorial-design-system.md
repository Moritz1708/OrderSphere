# 0013 — Single editorial design system replacing multi-brand theming

**Status:** Proposed

## Context

The frontend shipped a "Flat & Focused" design system with six selectable brands
(`ThemeState.Brands`) and a dark mode. An audit before the redesign found that the
system cost more than it delivered:

- **The brands were never used.** No tenant, environment, or customer selected one;
  the switcher was a header control that only changed the primary colour family.
  Every design decision had to survive six primaries, which ruled out colour fields,
  a second accent, and any composition that depends on a known palette.
- **Dark mode was half-broken.** Nine CSS blocks were keyed on `[data-mud-theme="dark"]`,
  an attribute MudBlazor never emits (verified against MudBlazor 9.9.0). Card shadows,
  scrollbars and hover states kept their light-mode values in dark mode.
- **The webfonts never loaded in production.** `index.html` linked Google Fonts while
  the BFF sends `style-src 'self'; font-src 'self' data:`, so production silently
  rendered in the system font stack.
- **`wwwroot/app.css` had drifted.** Roughly half of its 734 lines were classes no
  `.razor` file referenced, and `docs/ui-conventions.md` documented several of those
  dead classes as canonical.
- **No motion or accessibility baseline.** Ten hard-coded transitions, two keyframes,
  no `prefers-reduced-motion` handling, and no page rendered an `<h1>` despite
  `<FocusOnNavigate Selector="h1" />`.

## Decision

Replace the multi-brand system with one light and one dark theme, expressed as a
token layer that both CSS and C# read from.

- **One palette, two modes.** `Services/DesignTokens.cs` holds the literal values;
  `wwwroot/css/tokens.css` holds the same values as custom properties. `TokensCssSyncTests`
  fails the build when they drift, and `DesignTokensContrastTests` asserts the WCAG
  ratios each token is documented to meet. `BrandDefinition` and `ThemeState.Brands`
  are removed.
- **`html[data-theme]` drives CSS; `MudThemeProvider` drives MudBlazor.** An external
  `js/theme-boot.js` runs synchronously in `<head>`, reads `localStorage["os-dark-mode"]`
  (falling back to `prefers-color-scheme`), and stamps the attribute before the first
  paint. `Program.cs` reads the same value back so the provider renders the right
  palette on its first render. System preference is sampled once at boot and never
  overrides a stored choice.
- **MudBlazor stays, layered underneath.** It remains the functional base for dialogs,
  snackbars, selects, tables, drawers, pickers and popovers. `wwwroot/css/app.css`
  declares `@layer mud, tokens, base, overrides, components, pages, motion, utilities`
  and imports `MudBlazor.min.css` into the lowest layer, so OrderSphere rules win on
  layer order alone — no `!important`, no specificity escalation.
- **An `Os*` component kit carries the visual identity.** Buttons, cards, sections,
  headings, prices, skeletons, empty and error states live in `Components/Ui` as plain
  Blazor components over native markup. No new package dependency.
- **Fonts are self-hosted.** Instrument Serif (display), Manrope (body) and Geist Mono
  (numerals and metadata) ship from `wwwroot/fonts` under SIL OFL 1.1, which the
  production CSP allows.
- **Motion is CSS with one small JS module.** `js/motion.js` (~100 lines) owns the
  `IntersectionObserver` for scroll reveals, the theme attribute and scroll locking.
  Everything visible is a CSS transition or keyframe driven by duration and easing
  tokens, and a global `prefers-reduced-motion` block disables all of it.

## Consequences

- The palette now exists in two places (C# and CSS). This is unavoidable — CSS cannot
  read C#, and `MudColor` cannot parse `color-mix()` — and is guarded by a test rather
  than by discipline.
- Importing MudBlazor's stylesheet costs one extra request hop. A `<link rel="preload">`
  covers the latency.
- The override layer targets MudBlazor's internal class names, so a major MudBlazor
  upgrade can require revisiting `mud-overrides.css`. The layered approach keeps those
  overrides in one auditable file instead of scattered across components.
- Adding a customer-specific colour scheme is no longer a configuration change. If
  multi-tenant theming becomes a real requirement, it needs a new decision, not a
  brand entry.
- Design work gets simpler and more expressive: a known palette allows colour fields,
  a second accent for links and focus, and compositions that assume specific hues.

## Alternatives considered

- **Keep multi-brand and restyle within it** — rejected: the constraint has no consumer
  and blocks the design direction. Reintroducing it later is a bounded change.
- **Adopt Tailwind** — rejected: a second styling system alongside MudBlazor's, plus a
  build step, for utilities a small `utilities.css` already covers.
- **Replace MudBlazor with a custom component library** — rejected for now: dialogs,
  date pickers, popover positioning and focus management are substantial to rebuild,
  and the override layer already yields the intended look.
- **CSS isolation (`.razor.css`) per component** — rejected: kit classes are a shared
  vocabulary used by pages and components alike, scoped styles need `::deep` for every
  MudBlazor rule, and a single greppable `components.css` is reviewable.
- **View Transitions API for page transitions** — rejected: `document.startViewTransition`
  must wrap the synchronous render-batch application, and Blazor WebAssembly (verified
  against the .NET 10 `blazor.webassembly.js`) exposes no hook for that. A CSS enter
  animation on a re-keyed wrapper achieves the same effect.

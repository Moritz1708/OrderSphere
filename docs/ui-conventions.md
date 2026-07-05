# OrderSphere — UI & Styling Guide

Binding reference for all visual, theming, MudBlazor, and CSS work in `src/Frontend/OrderSphere.Web`.

The design direction is **"Flat & Focused"**. It ships as a multi-brand system: the **Electric** brand is
the default; five further variants (Lime, Sage, Royal, Solar, Mint) are selectable (see §1a). Only the
primary colour family changes between brands — every other rule below holds for all brands. This document
is the source of truth; where it and older notes disagree, this document wins.


The canonical implementations are:

- Theme (palette, typography, layout): `src/Frontend/OrderSphere.Web/Services/ThemeState.cs`
- Design tokens and component classes: `src/Frontend/OrderSphere.Web/wwwroot/app.css`

Change those two files first; this guide documents what they contain.

---

## 1. Design principles

- **Flat surfaces, no chrome.** `Elevation="0"` everywhere; separation comes from 1px dividers
  and the `--os-shadow-*` token shadows, not Material elevation.
- **One gradient.** The primary gradient appears on the hero and CTA sections only
  (`--os-gradient-primary` / `--os-gradient-primary-reverse`). It is built from the active brand's
  primary, so it follows a brand switch automatically. Everything else is a flat surface.
- **Two type roles.** Space Grotesk for display/headings, JetBrains Mono for numeric and
  metadata (prices, quantities, category/eyebrow labels, legal links).
- **Dark mode is supported.** A `PaletteDark` and `[data-mud-theme="dark"]` token overrides exist
  in `ThemeState.cs` / `app.css` and are surfaced via the `DarkModeToggle` component in the header.
  The preference is persisted to `localStorage` under `os-dark-mode` (device-local, works
  anonymously) and, for authenticated users, mirrored to `CustomerProfile.DarkModeEnabled`
  server-side so it follows the user across devices. `MainLayout` restores the local value first
  (no flash) and falls back to the server value only when no local value exists yet (first login on
  a new device). (The brand switch in §1a is a separate, orthogonal control.)
- **Tokens over hardcoded values.** Use `var(--mud-palette-*)` for theme colors and
  `var(--os-*)` for radii, shadows, gradients, and spacing. For primary-coloured fills use the
  `--os-primary-tint{-weak,-strong}` tokens — never literal `rgba()` of a brand colour, or the fill
  will not follow a brand switch. Avoid literal hex in components.

---

## 1a. Brands (multi-brand)

The app supports multiple brands. A brand only redefines the **primary colour family**
(`Primary`, `PrimaryDarken`, `PrimaryLighten`, `PrimaryText`); typography, layout, neutral greys,
dividers and the semantic colours (`Success`, `Warning`, `Error`) are shared across all brands.

Brands are declared in `ThemeState.Brands` (`ThemeState.cs`):

| Id | Name | Primary | PrimaryDarken | PrimaryLighten | PrimaryText | ContrastText |
|---|---|---|---|---|---|---|
| `electric` *(default)* | Electric | `#6260FF` | `#4A48CC` | `#E4E4FF` | `#6260FF` | `#FFFFFF` |
| `lime` | Lime | `#9FE870` | `#163300` | `#C4F5A7` | `#163300` | `#163300` |
| `sage` | Sage | `#BDD9D7` | `#03363D` | `#D8EDEC` | `#03363D` | `#03363D` |
| `royal` | Royal | `#3447AA` | `#253592` | `#FBEAEB` | `#3447AA` | `#FFFFFF` |
| `solar` | Solar | `#FCDB32` | `#141D38` | `#FEF08A` | `#141D38` | `#141D38` |
| `mint` | Mint | `#34E0A1` | `#000000` | `#87EEC8` | `#000000` | `#000000` |

`BrandDefinition` carries a `PrimaryContrastText` field (defaults `"#FFFFFF"`). Brands with light primaries (Lime, Sage, Solar, Mint) set a dark contrast value so button labels remain legible.

`PrimaryText` is a separate token: the color to use for **brand-colored text on light/white
surfaces** (eyebrows, footer/nav hover, prices, the white pill button), exposed as
`--os-primary-text` in `app.css` (via MudBlazor's otherwise-unused `Tertiary` palette slot). It is
independent of `PrimaryContrastText`, which answers a different question — the color of text
painted *on top of* a Primary-colored background. For brands whose `Primary` itself already has
≥4.5:1 contrast against white (Electric, Royal), `PrimaryText` equals `Primary`; for the light
brands it equals the same dark value as `PrimaryContrastText`.

Mechanics:

- `ThemeState.SetBrand(id)` rebuilds the `MudTheme` and raises `OnChange`; `MudThemeProvider` re-emits
  `--mud-palette-*`. CSS that uses `var(--mud-palette-primary)` and the `--os-primary-tint*` tokens
  updates automatically — no per-brand CSS block exists or should be added.
- The selection is surfaced via the `BrandSwitcher` component in the header and persisted to
  `localStorage` under the key `os-brand`; it is restored on first render in `MainLayout`.

**Adding a brand:** append one `BrandDefinition` to `ThemeState.Brands`. Nothing else is required —
do not add brand-specific CSS. Verify white-on-gradient hero text still has adequate contrast for the
chosen primary (the hero text helpers are fixed white), and choose a `PrimaryText` value with at
least 4.5:1 contrast against white (verify with the `PrimaryText_MeetsContrastRatioAgainstWhite`
test in `ThemeStateTests` or a contrast checker) — this is the value used for brand-colored text on
light surfaces and must be checked independently of `PrimaryContrastText`.

---

## 1b. Internationalization (i18n)

User-facing text is localized, not hardcoded. The supported UI languages are German (`de-DE`, the
**neutral** resource and default) and English (`en-US`), declared in
`Services/SupportedCultures.cs`. The active culture is resolved once at startup in `Program.cs` from
`localStorage["os-culture"]` (falling back to the default) and surfaced via the `CultureSwitcher`
component in the header; changing it persists the choice and force-reloads so every string re-resolves.

Mechanics:

- Strings live in `Resources/AppStrings.resx` (German, neutral) and `Resources/AppStrings.en.resx`
  (English), keyed by dotted names (`Cart.Title`, `Checkout.Submit`). The marker type is
  `AppStrings` at the root namespace.
- Inject `IStringLocalizer<AppStrings>` (conventionally as `L`) and read `@L["Key"]`; pass arguments
  for composite strings (`@L["Cart.AriaLabel", count]`) — never concatenate translated fragments.
- Culture-dependent values use `Services/Formatting.cs`: `Formatting.Currency(value)` (EUR, current
  culture's number layout), `Formatting.DateTime`/`Formatting.Date`. Do not call `ToString("C")` or
  hardcode `"dd.MM.yyyy"` / `de-DE` in components.

**Adding a string:** add the key to both `.resx` files (every key must exist in the neutral resource),
then reference it through the localizer. A missing English entry falls back to the German neutral value.

> Status: all customer-facing and admin pages are localized — header/navigation, footer, home, shop,
> categories, search, product details, product card, stock badge, order summary, cart, cart drawer,
> checkout (incl. address/payment forms), checkout success, all account pages (orders, order detail,
> profile, onboarding), and all admin pages (dashboard, orders, order detail, categories, category
> form, products, product form, users). Date and currency call-sites use `Formatting.DateTime/Date/Currency`
> throughout. A test (`LocalizationTests`) enforces that every neutral key has an English entry.

---

## 2. Palette (`ThemeState.cs`, `PaletteLight`)

Values below are the **Electric** (default) brand. `Primary`, `PrimaryDarken`, `PrimaryLighten`, and
`PrimaryContrastText` vary per brand (§1a); all other tokens are shared.

| Token | Value | Use |
|---|---|---|
| Primary | `#6260FF` | Buttons, links, accents, focus |
| PrimaryDarken | `#4A48CC` | Gradient end, hover |
| PrimaryLighten | `#E4E4FF` | Subtle accents, hover borders |
| Secondary | `#0D0D18` | Off-black; `section-dark`, strong text |
| Background | `#FFFFFF` | Page background |
| BackgroundGray | `#F1F2F8` | Alternating sections, inert fills |
| Surface | `#FFFFFF` | Cards, paper |
| TextPrimary | `#0D0D18` | Headings, primary text |
| TextSecondary | `#5A5A72` | Muted body / captions |
| Success | `#2EA04B` | Stock OK, free shipping |
| Warning | `#FF9F0A` | Low stock |
| Error | `#FF3B30` | Errors, destructive actions |
| Info | `#6260FF` | Informational (same as Primary) |
| Divider | `#DCDDE6` | Borders, separators |
| DividerLight | `#E8E9F0` | Hairline rows |
| `--os-primary-text` | `#6260FF` | Derived (repurposes MudBlazor's `Tertiary` slot). Brand-colored text on light/white surfaces — eyebrow, footer/nav hover, price, pill-button text. Use instead of raw `Primary` for text (§1a) |

### Dark mode (`ThemeState.cs`, `PaletteDark`)

`Primary`/`PrimaryDarken`/`PrimaryLighten`/`Info` are unchanged from the active brand; every other
token is dark-mode specific and shared across brands:

| Token | Value | Use |
|---|---|---|
| Secondary | `#EEEEF4` | Off-white; strong text on dark surfaces |
| Background | `#0D0E14` | Page background |
| BackgroundGray | `#15161F` | Alternating sections, inert fills |
| Surface | `#1C1E2A` | Cards, paper |
| TextPrimary | `#EEEEF4` | Headings, primary text |
| TextSecondary | `rgba(238,238,244,0.70)` | Muted body / captions |
| Success | `#3DB85F` | Stock OK, free shipping |
| Warning | `#FF9F0A` | Low stock (unchanged) |
| Error | `#FF453A` | Errors, destructive actions |
| Divider | `rgba(238,238,244,0.14)` | Borders, separators |
| DividerLight | `rgba(238,238,244,0.08)` | Hairline rows |

---

## 3. Typography

Font stacks are defined in `ThemeState.cs`:

- **Display / headings:** `Space Grotesk → Inter → system sans`. Helper class: `.os-display`.
- **Monospace / numeric:** `JetBrains Mono → ui-monospace → monospace`. Helper class: `.os-mono`.

Headings (H1–H6) use Space Grotesk at weight 600 with negative letter-spacing (tightest on
H1/H2). Buttons use `TextTransform = "none"`, weight 600 — never uppercase button labels.

Monospace is reserved for:

- Prices — `.os-price` (tabular numerals, weight 700) or `.os-mono`.
- Eyebrows — `.os-eyebrow` (mono, 11.5px, uppercase, leading rule, accent color).
- Category labels and the footer legal links.

---

## 4. Layout & radii

`LayoutProperties`: `DefaultBorderRadius = 12px`, `AppbarHeight = 72px`.

Radius tokens (`app.css`):

| Token | Value | Use |
|---|---|---|
| `--os-radius-sm` | 8px | Small chips, thumbnails |
| `--os-radius-md` | 14px | Inputs, inner cards |
| `--os-radius-lg` | 20px | Page cards, panels |
| `--os-radius-pill` | 100px | Buttons, quantity controls |

Section vertical rhythm uses `--os-section-py-sm|md|lg` (60/80/100px).

---

## 5. Section variants (`app.css`)

| Class | Background |
|---|---|
| `section-hero` / `section-hero-sm` | Indigo gradient (`--os-gradient-primary`) |
| `section-cta` | Reversed indigo gradient |
| `section-gray` | `BackgroundGray` |
| `section-white` | `Surface` |
| `section-dark` | `Secondary` off-black |

On gradient/dark backgrounds use the hero text helpers: `.hero-title`, `.hero-title-lg`,
`.hero-subtitle`, `.hero-subtitle-muted`, `.text-on-dark`, `.text-on-dark-muted`, and the
`.hero-pill-soft` chip. The `PageHero` component wraps the common hero layout.

---

## 6. Components

### Buttons
| Context | Class |
|---|---|
| Standard CTA | `btn-pill` |
| On gradient/dark background | `btn-pill-white` / `btn-pill-outline-white` |
| Neutral outline on light surface | `btn-pill-outline` |
| Form submit | `btn-pill` + `FullWidth="true"` |

### Surfaces
- Use `surface-card`, `surface-card-sm`, `surface-card-lg` (border + radius + surface) instead
  of `MudPaper` elevation. Add `surface-card-row-hover` for clickable rows.
- Product cards: `product-card` with `product-card-image` / `product-card-body`; the round add
  button is `product-add-btn`.
- Category grid: `category-card`; flat category strip: `category-strip-*`.

### Header & footer
- Header: `MudAppBar Elevation="0" Class="header-appbar"` (frosted glass). Outlined icons only.
  Logo mark: `.os-logo-mark`. Active nav underline: `.nav-link-active`.
- Footer: `.footer-main`, `.footer-link`, `.footer-bottom`, `.footer-legal-link` (mono).

### Checkout & cart
- Reusable summary: the `OrderSummary` component (cart and checkout share it). Set
  `ShowLineItems` to render the itemised list; pass page-specific buttons via the `Actions`
  fragment.
- Checkout sub-forms: `CheckoutAddressForm` and `CheckoutPaymentForm`, both bound to the shared
  `CheckoutFormModel`.
- Checkout CSS: `checkout-section`, `checkout-step-num`, `shipping-option`, `radio-dot`.

### Status, misc
- Stock: `stock-ok`, `stock-low`, `stock-out`; free shipping: `shipping-free`.
- Icon bubbles: `icon-bubble-primary|success-soft|on-hero`; initials: `avatar-initials`.
- Star rating color: `--os-star-gold` (brand-independent, no dark-mode override).
- Destructive zones: `danger-zone` / `danger-zone-title`.

---

## 7. Text emphasis

Use `Class="text-muted"` (maps to `TextSecondary`) for muted body and caption text rather than
`Color="Color.Secondary"`. In this palette `Secondary` is near-`TextPrimary` off-black and
produces no hierarchy. Use `text-strong` for emphasis back to primary text.

---

## 8. Fonts (preconnect in `index.html`)

```html
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;600;700&family=JetBrains+Mono:wght@400;500;600;700&display=swap" rel="stylesheet">
```

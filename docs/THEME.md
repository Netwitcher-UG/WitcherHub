# Theme

The user interface is built on **WowDash**, a purchased Bootstrap 5 admin
template. It replaced **Maxton**, an earlier purchased template.

## Where the files are

| Path | What it is |
| --- | --- |
| `WitcherHub/wwwroot/wowdash/css/style.css` | The theme. Vendor file — do not edit. |
| `WitcherHub/wwwroot/wowdash/css/remixicon.css` | Remix Icon font, the only icon family. Vendor file. |
| `WitcherHub/wwwroot/wowdash/css/lib/bootstrap.min.css` | Bootstrap 5, loaded **before** `style.css`. |
| `WitcherHub/wwwroot/wowdash/js/app.js` | Sidebar collapse, mobile drawer, light/dark toggle. Vendor file. |
| `WitcherHub/wwwroot/css/site.css` | Everything this application adds on top. **Edit here.** |
| `WitcherHub/wwwroot/css/ui-kit.css` | Toast notifications raised from `wwwroot/js/ui-kit.js`. |
| `WitcherHub/wwwroot/img/` | Company artwork. Survives a theme change; theme folders do not. |

Only the parts of the template the application uses are in the repository.
WowDash also ships chart, calendar, rich-text, slider, data-table and vector-map
plugins, plus about five megabytes of demo imagery; none of it appears in any page
here, so none of it is committed. Adding a page that needs one of those means
copying that one file in from the vendor archive.

## How a page gets the theme

Three layouts, one source of truth for assets:

| Layout | Used by |
| --- | --- |
| `_Layout.cshtml` | Every signed-in page. Sidebar, top bar, footer. |
| `_AuthLayout.cshtml` | Login, forgot password, reset password. |
| `_ContractsLayout.cshtml` | Pages a customer opens from an email — contract and quote signing. Deliberately bare. |

All three include `_ThemeHead.cshtml` and `_ThemeScripts.cshtml`. Nothing else may
declare a stylesheet or a script tag for the theme. The previous three layouts each
wired their own list and had already drifted: the signing page pulled in two icon
fonts nothing on it used, and the login page loaded a stylesheet the dashboard did
not. `ThemeAssetIntegrityTests` fails the build if a layout stops using the
partials.

## Conventions

**Icons — Remix Icon only.** `<i class="ri-delete-bin-line"></i>`. The previous
theme mixed three families (Bootstrap Icons, Boxicons, Material Icons Outlined),
two of them fetched from a CDN on every page load. WowDash's own markup uses
`<iconify-icon>`, which resolves glyphs over the network at runtime; that is not
used here — the Remix font is served from this application. A class name that is
not in `remixicon.css` fails a test.

**Colours — theme variables, never literals.** `var(--brand)`,
`var(--text-primary-light)`, `var(--text-secondary-light)`, `var(--border-color)`,
`var(--white)`, `var(--primary-50)`. Each is redefined under `[data-theme=dark]`,
so a rule written once works in both palettes. Hard-coded colours are what made
the previous theme's light mode unusable and needed four
`html[data-bs-theme=…]` override blocks to patch.

**Do not use Bootstrap's `text-light`.** It is near-white; it was legible only
because the previous theme was dark. Use `text-primary-light` for body text and
`text-secondary-light` for muted text. A test enforces this.

**Light and dark.** The palette is switched by `data-theme="light|dark"` on
`<html>`, chosen by the button in the top bar and remembered in `localStorage`
under `theme`. `_ThemeHead.cshtml` applies the stored value before the first paint.
The previous theme used `data-bs-theme` with five variants; any rule still keyed to
that attribute is dead, and a test says so.

**Headings.** The theme sizes headings with `clamp()` against the viewport, which
puts an `<h3>` at about 51px on a desktop. `site.css` pins them to a fixed scale
inside `.dashboard-main-body`; page titles are `<h3>`. The signing pages keep the
theme's own scale, where a large heading is what you want.

**Shared button variants** — `wh-btn-primary`, `wh-icon-btn`, `wh-icon-btn-plain`
— live in `site.css`. They were previously copy-pasted into inline `<style>` blocks
on seven pages, each hard-coding the old purple.

## Gotchas in the vendor CSS

- `style.css` opens with `*:where(…) { all: unset; display: revert; }`. It has zero
  specificity, so Bootstrap and `site.css` both win — but it does strip defaults
  nothing else sets. `<summary>` loses its disclosure marker this way;
  `site.css` puts one back.
- Every heading size in `style.css` carries `!important`, so overriding one
  requires `!important` too.
- Bootstrap paints table cells with `--bs-table-bg`, which is white in both
  palettes. `site.css` clears it on `thead th`, or the header row is a white band
  in dark mode.
- `style.css` imports the Inter font from Google Fonts. It is the only external
  request any page makes.

## Shared components

Built on top of the theme, in `WitcherHub/Pages/Shared` and
`WitcherHub/Pages/Models/UI`:

| Component | What it is |
| --- | --- |
| `_StatusBadge.cshtml` + `StatusPresentation.cs` | The single place that decides how a status looks. `DocumentStatus` is shared by quotes, contracts and invoices but means different things on each — "Sent" is awaiting a decision on a quote and awaiting a signature on a contract — so wording and colour are chosen per document kind. |
| `_StatCard.cshtml` + `StatCardVm` | A headline figure on the dashboard. |
| `_AttentionList.cshtml` + `AttentionListVm` | A "needs attention" panel. Words a day count in the right direction: the same 12 days is "12 days late" for an overdue invoice and "in 12 days" for a contract about to end. |
| `_EmptyState.cshtml` + `EmptyStateVm` | Shown instead of an empty table. Distinguishes "nothing here yet" from "nothing matches the filter", because an empty grid otherwise reads as a failure. |
| `_RegisterFilters.cshtml` + `RegisterFilterVm` | The filter bar above the quote, contract and invoice registers. A plain GET form, so a filtered view can be bookmarked. |
| `_RegisterPager.cshtml` + `PagerVm` | Paging that rebuilds its links from the live query string, so a new filter is never dropped on page two. |
| `Format.cs` | Money and dates. Amounts always use German separators, whatever culture the request ran under, because the figure on screen has to match the figure on the invoice. |

`ApexCharts` (`wowdash/js/lib/apexcharts.min.js`) is loaded by the dashboard only —
it is half a megabyte and nothing else charts anything.

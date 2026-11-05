# Email design system

Every transactional email is built from one layout, a small set of components and a single token source. The goal is
the consistency of a product design system: a rebrand or a dark-mode tweak touches **one** file, never 30 templates.

## Pieces

| Piece | Where | What |
|---|---|---|
| Tokens | [`EmailDesignTokens.cs`](../../src/Modules/Notification/Notification/Channels/Email/EmailDesignTokens.cs) | Semantic colors (`text`, `textMuted`, `brand`, `danger`, ...), dark-scheme colors (`dark*`), type scale, spacing, radii, font stacks. Exposed to Liquid as `t` (`{{ t.textMuted }}`). |
| Brand overrides | `Email:Theme` ([`EmailThemeOptions`](../../src/Modules/Notification/Notification/Channels/Email/EmailDesignTokens.cs)) | `PrimaryColor`, `OnPrimaryColor`, `AccentColor` (6-digit hex; invalid values keep the default), `LogoUrl`, `LogoWidth`, `HelpCenterUrl`, `PrivacyUrl`, `CompanyAddress`. `Email:BrandName`, `SupportEmail` as before. |
| Layout | `Templates/Layouts/BaseLayout.liquid` | 600px card, logo or text brand, hidden preheader, footer (support, help/privacy links, address), light + dark scheme, RTL through `dir`. |
| Components | `Templates/Components/` | see below |
| Plain text | [`EmailPlainText`](../../src/Modules/Notification/Notification/Channels/Email/EmailPlainText.cs) | every mail is `multipart/alternative`: a text part derived from the HTML (links as `label (url)`), then the HTML. |

Globals available everywhere: `culture`, `dir` (`ltr`/`rtl`), `align` / `alignEnd` (logical start/end),
`brandName`, `appBaseUrl`, `supportEmail`, `currentYear`, `logoUrl`, `t`.

## Components

Include with `{% include 'Components/<Name>' %}` after assigning the parameters.

| Component | Parameters | Use |
|---|---|---|
| `Heading` | `heading_text` | the email title |
| `Text` | `text_content` (encoded) or `text_html` (trusted markup written in the template); `text_tone` = body / muted / small | paragraphs |
| `Button` | `button_url` (always through `safe_url`), `button_text`, `button_variant` = primary / success / danger / secondary | call to action (VML for Outlook) |
| `Alert` | `alert_text_en`, `alert_text_ar`, `alert_type` = info / success / warning / danger | notices |
| `Card` | `card_content` (captured), `card_title` | grouped data |
| `KeyValue` | `kv_label`, `kv_value`, `kv_strong` | label/value rows inside a Card (amounts, dates, references) |
| `CodeBlock` | `code_value`, `code_caption` | one-time codes (monospace, spaced, `dir=ltr`) |
| `Badge` | `badge_text`, `badge_tone` | inline status pill |
| `Divider`, `Spacer` | `spacer_size` | rhythm |

Preheader (inbox preview line): `{{ 'Your invoice is ready' | set_preheader }}` anywhere in the template. Never put
secrets (codes, links with tokens) in it - lock screens show it.

Reference templates built only from components: [`SecurityAlert.liquid`](../../src/Modules/Notification/Notification/Channels/Email/Templates/SecurityAlert.liquid),
[`OneTimeCode.liquid`](../../src/Modules/Notification/Notification/Channels/Email/Templates/OneTimeCode.liquid).
Older templates keep their markup but use tokens and the `sk-h` / `sk-p` dark-mode classes.

## Dark mode

Inline styles carry the light scheme (every client); the layout re-colors elements with `sk-*` classes under
`prefers-color-scheme: dark` (Apple Mail, iOS, Outlook for Mac/iOS/Android) and `[data-ogsc]/[data-ogsb]`
(Outlook.com). Gmail applies its own inversion. Classes: `sk-bg`, `sk-card`, `sk-body`, `sk-footer`, `sk-h`,
`sk-p`, `sk-muted`, `sk-box`, `sk-code`.

## Rules (enforced by [`EmailDesignSystemTests`](../../src/Modules/Notification/Notification.Tests/Email/EmailDesignSystemTests.cs))

- No hex color in any `.liquid` file - use a token; add a token in `EmailDesignTokens` if a new role is needed.
- Every template renders in `en` and `ar` with no unresolved Liquid, no empty style value (a misspelled token), the
  dark-mode block and the right `dir`.
- The plain-text part is readable (no markup, no runs of blank lines).
- Unchanged from before: model values are HTML-encoded; every `href` goes through `safe_url`.

Adding a template: `/starterkit:add-email-template` (enum value, model, subject class, `.liquid` from components).

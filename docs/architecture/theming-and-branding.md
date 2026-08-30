# Theming And Branding

Stand: 2026-08-29

Das Theme-System liegt in `src/B2B.Portal.Web/src/themes`.

## Dateien

- `theme.schema.ts`: `PortalThemeDefinition`.
- `theme-loader.ts`: Validierung, sicherer Default und Fluent-Theme-Mapping.
- `corporate-vibrant.theme.ts`: Demo-Theme `Corporate Vibrant`.
- `functional-minimal.theme.ts`: Demo-Theme `Functional Minimal`.

## Sicherheitsregel

Tenant Branding erlaubt nur validierte Design Tokens. Freies CSS, JavaScript-Snippets,
unsanitized HTML und beliebige Script-URLs sind nicht implementiert.

## Tenant-Zuordnung

Die API liefert `GET /api/ui/configuration`. In `LOCAL_MOCK` kann ein Dev-Header
`X-Portal-Theme-Id` ein gebuendeltes Theme auswaehlen. Unbekannte Theme IDs fallen auf
`corporate-vibrant` zurueck.

Produktive Tenant-Theme-Zuordnung: `integration pending`.


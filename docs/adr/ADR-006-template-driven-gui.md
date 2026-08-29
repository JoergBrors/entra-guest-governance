# ADR-006: Template Driven GUI

Datum: 2026-08-29

## Status

Accepted

## Kontext

Die GUI muss tenantfaehiges Branding unterstuetzen, ohne React-Komponenten pro Kunde zu forken oder freie CSS-/JavaScript-Injektion zuzulassen.

## Entscheidung

Die Webapp nutzt `PortalThemeDefinition` als kontrolliertes Token-Modell. `theme-loader.ts` validiert gebuendelte Theme IDs, faellt bei unbekannten IDs auf `corporate-vibrant` zurueck und mapped Tokens auf Fluent UI Theme-Werte sowie kontrollierte CSS-Variablen.

## Konsequenzen

- Business-Komponenten werden nicht pro Theme dupliziert.
- Tenant Branding bleibt auf validierte Tokens begrenzt.
- Produktive Tenant-Theme-Zuordnung bleibt ein API-/Konfigurationsthema.


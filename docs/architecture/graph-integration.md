# Graph Integration

Stand: 2026-08-29

Graph-Zugriff ist ueber Ports vorbereitet:

- `IGuestDirectory`
- `IResourceConnector`
- `IEmailProvider`

Aktueller Directory Provider:

- `MockGuestDirectory`

Echter Microsoft Graph Directory Provider: `integration pending`.

Direkte Graph-Abhaengigkeiten in Domain/Application sind laut Architekturtests nicht erlaubt.


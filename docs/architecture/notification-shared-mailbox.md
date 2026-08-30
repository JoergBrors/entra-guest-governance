# Notification Shared Mailbox

Stand: 2026-08-29

Notification-Abstraktion:

- `IEmailProvider`
- `EmailMessage`
- `MockEmailProvider`
- `GraphSharedMailboxEmailProvider`

`GraphSharedMailboxEmailProvider` ist als Schale vorhanden und blockiert ohne `ALLOW_GRAPH_WRITES`.

Produktive Shared Mailbox: `configuration required`.


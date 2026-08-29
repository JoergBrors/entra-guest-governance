# Local Mock

Stand: 2026-08-29

`LOCAL_MOCK` bleibt der Default fuer lokale Entwicklung.

Dev-Header fuer die neue UI-/Auth-Schicht:

- `X-Platform-Tenant-Id`
- `X-Portal-User-Mail`
- `X-Portal-Roles`
- `X-Scenario-Manager-Workload-Ids`
- `X-Portal-Theme-Id`

Der Web-Client setzt lokale Defaultwerte aus Vite-Env-Variablen:

- `VITE_DEV_PLATFORM_TENANT_ID`
- `VITE_DEV_PORTAL_USER_MAIL`
- `VITE_DEV_PORTAL_ROLES`

Produktive Werte: `configuration required`.


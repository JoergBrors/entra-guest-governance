# Prompt 03 — Vollständigkeitsprüfung + Entra/Bicep-Automatisierung

- **Datum:** 29. August 2026
- **Auftrag:** Chat-Auftrag (kein `prompts/*.md`-Datei-Prompt): Repository auf
  Vollständigkeit prüfen, pro-Prompt-Dokumentation einführen, prüfen ob Bicep +
  PowerShell/Microsoft Graph die Entra-ID-Voraussetzungen herstellen können — **ohne**
  in der Entwicklung Azure-Ressourcen anzulegen —, `.env`-Handling (lokal, nicht
  committed) und `.env.example` (Werte + Ursprung) sicherstellen.
- **Ausführungsumgebung:** lokale Windows-Entwicklungsumgebung mit vollem Tooling
  (`dotnet 10.0.303`, Node/npm, Bicep CLI 0.46.1, Azure CLI).

## Was geprüft wurde

1. **Repository-Vollständigkeit:** alle in Prompt 01 geforderten Bestandteile (Struktur,
   Domain/Application/Infrastructure/Worker/Api/Web, Tests, Infra, Prompts, Docs) sind
   vorhanden.
2. **Backend-Build/-Test (erstmals mit echtem `dotnet`):**
   - `dotnet restore` ✅
   - `dotnet build -c Debug` ❌ zunächst 3 Fehler (CS9113, ungelesene
     Primary-Constructor-Parameter) → behoben → ✅ 0 Fehler/0 Warnungen
   - `dotnet test -c Debug` ✅ 31/31 Tests grün
3. **Frontend-Build/-Test:** `npm ci`, `npm run build`, `npx vitest run` ✅ (bestätigt
   Prompt-01/02-Ergebnisse erneut).
4. **Bicep-Validierung:** `az bicep build --file infra/main.bicep --stdout` ✅ kompiliert
   fehlerfrei — rein lokale Kompilierung, **keine** Azure-Ressourcen wurden erzeugt.
5. **`.env`-Handling:** keine `.env`-Datei im Repo vorhanden (nur `.env.local`, per
   `.gitignore` ausgeschlossen); `.env.example` (Root) und
   `src/B2B.Portal.Web/.env.example` bereits vorhanden und mit Kommentaren zum Ursprung
   jedes Werts versehen — entsprach bereits der Anforderung, wurde um Skript-Hinweise
   ergänzt.

## Was geändert/ergänzt wurde

### Bugfixes (Backend, minimal-invasiv)

- `src/B2B.Portal.Application/Services/LifecycleService.cs` — ungenutzten `IClock clock`-
  Parameter entfernt (Timestamps laufen bereits über `AuditService`).
- `src/B2B.Portal.Worker/Handlers/Reviews/ReviewHandlers.cs` — ungenutzten
  `IAssignmentRepository assignmentRepository`-Parameter in `ApplyReviewDecisionHandler`
  entfernt.
- `src/B2B.Portal.Worker/Handlers/Provisioning/ProvisioningHandlers.cs` — ungenutzten
  `IAssignmentRepository assignmentRepository`-Parameter in `RevokeWorkloadRoleHandler`
  entfernt.
- `src/B2B.Portal.Worker/Handlers/Lifecycle/LifecycleHandlers.cs` — ungenutzten
  `IJobRepository jobRepository`-Parameter in `ValidateDeletionHandler` entfernt.

### Neu: Entra-ID-Voraussetzungen per PowerShell/Microsoft Graph

- **`scripts/setup-entra-app.ps1`** (neu) — legt per Microsoft Graph PowerShell SDK eine
  App Registration mit den benötigten Application Permissions an
  (`User.Invite.All`, `Mail.Send`, `Group.ReadWrite.All`, `User.Read.All`), erstellt ein
  Client Secret und versucht Admin Consent zu erteilen. **Default: Dry-Run** (`-WhatIf`-
  Charakter) — erst mit explizitem `-Apply` werden tatsächlich Entra-Objekte angelegt.
  Optional `-WriteEnvLocal`, um die erzeugten Werte direkt nach `.env.local` zu schreiben
  (nie committed). Erstellt **keine** Azure-Compute-/Storage-Ressourcen — ausschließlich
  Entra-ID-Objekte (App Registration, Service Principal, App Role Assignments).
- **`scripts/sync-keyvault.ps1`** (neu) — liest Secret-relevante Keys aus `.env.local` und
  spiegelt sie optional in einen bereits existierenden Azure Key Vault. **Default:
  Dry-Run**, erst `-Apply` schreibt tatsächlich. Erstellt selbst keinen Key Vault.
- **`infra/modules/key-vault.bicep`** (neu) — Bicep-Modul für einen Key Vault mit
  RBAC-Autorisierung (`Key Vault Secrets Officer`-Rollenzuweisung für einen optionalen
  Principal). Schreibt selbst keine Secret-Werte (das bleibt `sync-keyvault.ps1`
  vorbehalten, damit kein Secret in der Bicep-Deployment-History landet).
- **`infra/main.bicep`** (geändert) — neuer Parameter `deployKeyVault` (**Default `false`**)
  und `keyVaultAccessPrincipalId`; der Key-Vault-Deployment-Block ist an `deployKeyVault`
  gekoppelt. Damit bleibt der bisherige Default-Deploy unverändert (Static Web App +
  Cosmos Free Tier + Automation) und es entstehen weiterhin **keine** zusätzlichen
  Azure-Ressourcen, solange nicht explizit `deployKeyVault=true` gesetzt wird.

### Dokumentation

- **README.md** — neuer Abschnitt "Entra-ID-Voraussetzungen automatisiert herstellen
  (DEV_INTEGRATION)" mit Nutzungsbeispielen für beide neuen Skripte.
- **`.env.example`** — Kommentarblock ergänzt, der auf die beiden neuen Skripte verweist.
- **`docs/architecture/mvp-test-report.md`** — aktualisiert mit den jetzt echten
  Build-/Testergebnissen (statt "nicht ausgeführt"), neuem Abschnitt 3 (behobene
  Kompilierfehler), aktualisierter Kriterientabelle, ergänztem Risiken-/Nächste-Schritte-
  Abschnitt.
- **`docs/prompts/`** (neu) — dieses Verzeichnis mit `README.md` (Index) sowie
  rückwirkenden Zusammenfassungen für Prompt 01 und 02.

## Was bewusst NICHT getan wurde

- Keine echten Entra-ID-Objekte angelegt (`setup-entra-app.ps1` nur im Dry-Run
  ausgeführt/beschrieben, nicht mit `-Apply` aufgerufen — kein Ziel-Tenant vorhanden).
- Kein `az deployment group create` / `New-AzResourceGroupDeployment` ausgeführt — Bicep
  wurde ausschließlich lokal kompiliert (`az bicep build`), keine Azure-Ressourcen
  erzeugt.
- Keine erfundenen Tenant-IDs, Client-Secrets oder Mailboxen — `.env.local` existiert
  weiterhin nicht im Repository.
- Kein API-Command für Workload-Erstellung ergänzt (bereits in Prompt-01/02-Reports als
  offener Punkt dokumentiert, bleibt es — außerhalb des heutigen Auftragsumfangs).

## Ergebnis

Build- und Testzustand des Repositories ist jetzt **real verifiziert** (nicht nur
spezifikationskonform geschrieben): 31/31 .NET-Tests grün, 2/2 Frontend-Tests grün, Bicep
kompiliert fehlerfrei. Die Entra-ID-Voraussetzungen können ab sofort reproduzierbar per
PowerShell/Microsoft Graph hergestellt werden, ohne dass dabei Azure-Ressourcen entstehen;
eine optionale, ebenfalls standardmäßig inaktive Kette bis zur Key-Vault-Spiegelung ist
vorbereitet.

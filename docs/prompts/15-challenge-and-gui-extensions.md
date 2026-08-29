# Prompt 15 - Challenge und GUI-Erweiterung

Datum: 2026-08-29

## Auftrag

Zwei angehaengte Prompts wurden getrennt von der Benutzeranfrage behandelt:

1. Challenge gegen den B2B Guest Governance Blueprint.
2. GUI-, Theme- und Dokumentationserweiterung.

Benutzerfreigabe:

- `FREIGABE E01 - E08`

Zusaetzliche fachliche Fakten:

- Rollen und Scopes auf Workgroup/Workload- und Szenario-Ebene.
- Workload Owner darf Szenarien anlegen, loeschen, editieren und den Workload modifizieren.
- Scenario Manager darf innerhalb seines Szenario-/Workload-Scopes agieren.
- GuestAccount ist die anmeldende Person.
- Sponsor verantwortet den GuestAccount.
- Bei mehreren Workloads erfolgen Aenderungen ueber Review-Prozesse.
- Delete Guest nur ohne Workload-Zuordnung.

## Schritte

1. Repository read-only analysiert.
2. Challenge-Bericht mit E01-E08 erstellt.
3. Nach Freigabe API Auth-/Scope-Kontext ergaenzt.
4. `/api/me/workloads`, `/api/me/navigation`, `/api/ui/configuration` ergaenzt.
5. Workload-, Szenario-, Review-, Audit- und Guest-Management-Endpunkte serverseitig geschuetzt.
6. DeleteGuest gegen vorhandene Workload-Zuordnungen und Deletion Gate abgesichert.
7. Review-Entscheidungsendpoint und Audit im Review-Handler ergaenzt.
8. Theme-System mit Corporate Vibrant und Functional Minimal ergaenzt.
9. Rollenbasierte App Shell und Development Theme Preview ergaenzt.
10. Fehlende MVP-Seiten additiv angelegt.
11. Markdown-Dokumentation und ADR-006 ergaenzt.

## Ergebnis

Keine produktiven Tenant-, Client-, Mailbox- oder Corporate-Design-Werte wurden erfunden.

## Folgeerweiterung: Admin Workload Create und Mock Entra

Umgesetzt:

- Admin kann Workloads per API und GUI erstellen.
- Admin kann Gaeste in der Workload-Admin-Ansicht Rollen zuweisen.
- Der LOCAL_MOCK enthaelt einen Entra-ID-Mock-Stamm mit Benutzern, Gruppen und Memberships.
- Worker-Discovery liest Benutzer und Gruppenmitgliedschaften aus dem Mock.
- Worker-Deploy kann Gruppen im Mock anlegen.
- Worker-Grant/Revoke weist Gruppenmitgliedschaften im Mock zu oder entfernt sie.
- Governance Admin kann den Mock-Entra-Stamm im Development-Portal unter `/dev/mock-entra` einsehen.
- API stellt im `LOCAL_MOCK` die Development-Endpunkte `/api/dev/mock-entra/users`, `/api/dev/mock-entra/groups` und `/api/dev/mock-entra/memberships` bereit.

Verifikation:

- `dotnet build -c Debug`: erfolgreich.
- `dotnet test -c Debug`: erfolgreich.
- `npm run build`: erfolgreich.
- `npm run test -- --run`: erfolgreich.

# Auftrag: B2B Guest Governance Portal - MVP Verification

Prüfe das bestehende Repository gegen die Development- und MVP-Anforderungen. Verändere die Architektur nicht grundlos und starte keine echten produktiven Integrationen.

REGELN
- .NET-Projekte müssen net10.0 verwenden.
- Default Modus ist LOCAL_MOCK.
- Keine echten Graph Writes, Guest Deletes oder realen E-Mail-Versände ohne explizite Testkonfiguration.
- Keine Secrets ausgeben oder committen.
- Domain/Application dürfen nicht von konkreten Graph/Azure-Implementierungen abhängen.

FÜHRE AUS UND DOKUMENTIERE
1. Repository-/Projektstruktur prüfen.
2. dotnet --info und verwendetes SDK dokumentieren.
3. dotnet restore, dotnet build und dotnet test ausführen.
4. Frontend npm ci, build und Tests ausführen.
5. API Health und wesentliche Query/Command Endpoints im LOCAL_MOCK Modus prüfen.
6. Worker starten und mindestens folgende Jobs Ende-zu-Ende durch Mock-Adapter verarbeiten:
   - InviteGuest
   - GrantWorkloadRole
   - SendNotification
   - StartReview / ApplyReviewDecision
   - RevokeWorkloadRole
   - ValidateDeletion (Dry Run)
7. Tenant-Isolation mit mindestens zwei Tenants negativ testen.
8. Idempotenz nachweisen: derselbe Grant-Job darf keinen doppelten technischen Zustand erzeugen.
9. Deletion Gate negativ testen:
   - aktive Workload-Zuordnung -> BLOCK
   - Unclassified Access -> BLOCK
   - offener sicherheitsrelevanter Job -> BLOCK
   - Connectorfehler -> BLOCK
   - Live Check meldet Access -> BLOCK
   - nur bei vollständig freiem Zustand -> ALLOW/READY, aber im LOCAL_MOCK niemals echten Delete ausführen
10. Notification Mock muss Sender, Recipient, Template, CorrelationId und Workload-Kontext nachvollziehbar protokollieren.
11. Prüfe, ob Graph Shared-Mailbox Provider vollständig konfigurationsgetrieben ist.
12. Prüfe Audit Events für sicherheitsrelevante Aktionen.
13. Führe einen finalen Quality-Gate-Lauf durch.

FEHLERBEHEBUNG
- Behebe Code-/Testfehler, sofern sie innerhalb der beschriebenen Architektur liegen.
- Wenn eine echte externe Integration erforderlich wäre, nicht halluzinieren: dokumentiere den fehlenden Tenant-/Credential-/Mailbox-Input und lasse den Test sauber als „integration pending" markiert.

ERGEBNIS
Aktualisiere docs/architecture/mvp-test-report.md mit:
- Datum/Commit
- ausgeführten Befehlen
- Build-/Testresultaten
- getesteten MVP-Kriterien
- Pass/Fail je Kriterium
- offenen Integrationstests
- Security-/Tenant-Isolation-Befunden
- bekannten Risiken
- konkreten nächsten Schritten

Am Ende gib eine kurze Zusammenfassung mit Gesamtstatus: PASS, PASS WITH PENDING INTEGRATIONS oder FAIL.

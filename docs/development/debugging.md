# Debugging

Stand: 2026-08-29

Relevante Pruefpunkte:

- API: `dotnet run --project src/B2B.Portal.Api`
- Worker: `dotnet run --project src/B2B.Portal.Worker`
- Web: `npm run dev --prefix src/B2B.Portal.Web`
- Health: `GET /health`

Bei `403`-Antworten zuerst Rollen-/Scope-Header im `LOCAL_MOCK` pruefen.


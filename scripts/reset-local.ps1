# Setzt die lokale Development-Umgebung zurueck (bin/obj/node_modules Caches).
Write-Host "Bereinige bin/obj..."
Get-ChildItem -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Bereinige Frontend node_modules/dist..."
Remove-Item -Recurse -Force "src/B2B.Portal.Web/node_modules" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "src/B2B.Portal.Web/dist" -ErrorAction SilentlyContinue

Write-Host "Fertig. Fuehre 'dotnet restore' und 'npm ci' erneut aus."

using Microsoft.Extensions.Configuration;

namespace B2B.Portal.Infrastructure;

/// <summary>
/// Laedt .env.local (falls vorhanden) in die Prozess-Umgebungsvariablen, bevor
/// AddEnvironmentVariables() die Konfiguration aufbaut. Damit greifen dieselben Werte, die
/// scripts/requirements.ps1 -InitCosmosEmulator bereits nach .env.local schreibt
/// (COSMOS_EMULATOR_ENDPOINT etc.), automatisch bei jedem "dotnet run" — nicht nur beim
/// VS-Code-Debug-Start ueber launch.json envFile. Bereits gesetzte Prozess-Umgebungsvariablen
/// werden NICHT ueberschrieben (.env.local ist nur ein lokaler Fallback-Default, kein
/// Override eines bewusst gesetzten Werts).
/// </summary>
public static class DotEnvLoader
{
    public static IConfigurationBuilder AddDotEnvLocal(this IConfigurationBuilder builder)
    {
        var path = FindEnvLocal();
        if (path is null)
        {
            return builder;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();
            if (value.Length == 0 || Environment.GetEnvironmentVariable(key) is not null)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
        }

        return builder;
    }

    // Sucht .env.local ausgehend vom aktuellen Arbeitsverzeichnis nach oben (bis zu 5
    // Ebenen) — deckt sowohl "dotnet run" aus dem Repo-Root als auch aus src/B2B.Portal.Api
    // heraus ab, ohne einen hartcodierten relativen Pfad zu brauchen.
    private static string? FindEnvLocal()
    {
        var dir = new DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
        for (var i = 0; i < 5 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".env.local");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}

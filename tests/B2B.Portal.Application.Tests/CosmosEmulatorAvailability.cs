using System.Net.Sockets;

namespace B2B.Portal.Application.Tests;

/// <summary>
/// Kurzer TCP-Connect-Check, ob der lokale Cosmos DB Emulator erreichbar ist (Default-Port
/// 8081, siehe scripts/requirements.ps1 -InitCosmosEmulator). Cosmos-spezifische Tests
/// überspringen sich selbst, wenn nicht — damit "dotnet test" ohne installierten/laufenden
/// Emulator CI-sicher bleibt, waehrend lokale Entwickler mit laufendem Emulator die echten
/// Roundtrip-Tests bekommen. Duplikat von B2B.Portal.Integration.Tests.CosmosEmulatorAvailability
/// (keine ProjectReference zwischen den beiden Testprojekten).
/// </summary>
public static class CosmosEmulatorAvailability
{
    public static bool IsRunning(string host = "localhost", int port = 8081, int timeoutMs = 500)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            return connectTask.Wait(timeoutMs) && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}

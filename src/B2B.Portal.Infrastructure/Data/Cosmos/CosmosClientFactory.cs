using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>
/// Baut den singleton CosmosClient aus derselben Konfiguration, die
/// scripts/requirements.ps1 -InitCosmosEmulator bereits nach .env.local schreibt
/// (COSMOS_EMULATOR_ENDPOINT/COSMOS_EMULATOR_KEY/COSMOS_DATABASE_ID). Nur relevant, wenn
/// DATA_PROVIDER=cosmos gesetzt ist — der InMemory-Default benötigt diese Klasse nicht.
/// </summary>
public sealed class CosmosClientFactory
{
    private readonly CosmosClient _client;
    private readonly Database _database;

    public CosmosClientFactory(IConfiguration configuration)
    {
        var endpoint = configuration["COSMOS_EMULATOR_ENDPOINT"];
        var key = configuration["COSMOS_EMULATOR_KEY"];
        var databaseId = configuration["COSMOS_DATABASE_ID"] ?? "b2b-governance-dev";
        var allowInsecureEmulatorTls = bool.TryParse(configuration["COSMOS_EMULATOR_ALLOW_INSECURE_TLS"], out var insecureTls)
            && insecureTls;

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "DATA_PROVIDER=cosmos erfordert COSMOS_EMULATOR_ENDPOINT und COSMOS_EMULATOR_KEY " +
                "(siehe .env.local, erzeugt durch scripts/requirements.ps1 -InitCosmosEmulator).");
        }

        // Gateway-Mode statt Direct-Mode: der Windows Cosmos DB Emulator nutzt ein
        // selbstsigniertes Zertifikat, das requirements.ps1 bereits fuer den REST-Layer
        // (SkipCertificateCheck) behandelt hat — Gateway-Mode zentralisiert dieselbe
        // Ausnahme fuer den SDK-Client, statt sie pro Request zu wiederholen.
        //
        // UseSystemTextJsonSerializerWithOptions statt SerializerOptions (mit denen sich
        // beide gegenseitig ausschliessen, siehe CosmosClientOptions-Validierung): der
        // eingebaute CosmosSerializationOptions-Typ kennt nur PropertyNamingPolicy, aber
        // keinen Weg, Enums als String statt als Zahl zu serialisieren. Ohne
        // JsonStringEnumConverter wurden Enum-Properties (z.B. AssignmentStatus) als
        // numerischer Index gespeichert, waehrend Repository-Queries wie
        // "c.status IN (@active, @approved, @requested)" gegen die STRING-Namen filtern —
        // ein stiller String-vs-Zahl-Mismatch, der ListActiveByGuestAsync/
        // ListByWorkloadAsync-mit-Statusfilter immer leer liefern liess (gefunden beim
        // Live-Test des Excel-Gaeste-Imports: ein Fremd-Workload-Review wurde nicht
        // erzeugt, obwohl eine aktive Zuweisung existierte).
        var clientOptions = new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() },
            },
        };

        var isEmulator = endpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("127.0.0.1", StringComparison.Ordinal);
        if (isEmulator || allowInsecureEmulatorTls)
        {
            clientOptions.HttpClientFactory = () =>
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                };
                return new HttpClient(handler);
            };
        }

        _client = new CosmosClient(endpoint, key, clientOptions);
        _database = _client.GetDatabase(databaseId);
    }

    public Container GetContainer(string name) => _database.GetContainer(name);
}

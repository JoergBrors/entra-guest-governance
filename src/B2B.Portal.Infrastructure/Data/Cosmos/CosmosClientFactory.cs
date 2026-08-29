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
        var clientOptions = new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
            },
        };

        var isEmulator = endpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("127.0.0.1", StringComparison.Ordinal);
        if (isEmulator)
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

using System.Text.Json;

namespace B2B.Portal.Domain.Entities;

/// <summary>
/// Szenario innerhalb eines Workload: ein per Template frei definierbares Set von
/// Ressourcen-Zuordnungen (ScenarioResourceRule). Anders als WorkloadRole gibt es keine
/// feste Firma/Umgebung auf Szenario-Ebene — jede Regel trägt ihre eigenen fachlichen
/// Schlüssel (Firma, Rolle, ...) im freien Fields-Dictionary, das später beim Excel-Import
/// als Bezugspunkt dient (eine Excel-Spalte matched gegen einen Fields-Schlüssel).
/// </summary>
public sealed class WorkloadScenario
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PlatformTenantId { get; init; }
    public required Guid WorkloadId { get; init; }
    public required string Name { get; set; }
    public List<ScenarioResourceRule> Rules { get; init; } = new();
    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Verknüpft eine WorkloadResource mit frei definierten fachlichen Schlüsseln (z. B.
/// Firma, Rolle) und einer optionalen Bedingung (JSONLogic, siehe JsonLogicEvaluator), unter
/// der diese Ressource tatsächlich deployt wird. Beim Template-Import wird die referenzierte
/// Ressource automatisch angelegt, falls sie unter (Name, Art) noch nicht existiert.
/// </summary>
public sealed class ScenarioResourceRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid WorkloadScenarioId { get; init; }
    public required Guid ResourceId { get; init; }
    public Dictionary<string, string> Fields { get; init; } = new();

    /// <summary>Null = Regel gilt immer (keine Bedingung).</summary>
    public JsonElement? Condition { get; set; }
}

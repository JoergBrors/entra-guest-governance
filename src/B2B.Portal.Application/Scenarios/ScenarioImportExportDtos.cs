using System.Text.Json;

namespace B2B.Portal.Application.Scenarios;

/// <summary>
/// Vollständiges, hochladbares Szenario-Template. Ressourcen werden per (ResourceName,
/// ResourceType) beschrieben — existiert im Ziel-Workload noch keine passende
/// WorkloadResource, wird sie beim Import automatisch angelegt. Fields ist ein frei
/// definiertes Set fachlicher Schlüssel (z. B. "Firma", "Rolle") je Regel, das später beim
/// Excel-Import als Bezugspunkt dient (eine Excel-Spalte matched gegen einen Fields-
/// Schlüssel; ein Wert kann mehrere Regeln gleichzeitig treffen). Condition ist optional
/// (null = Regel gilt immer) und ein rohes JSONLogic-Dokument (siehe JsonLogicEvaluator).
/// </summary>
public sealed record ScenarioTemplateRuleDto(
    string ResourceName,
    string ResourceType,
    IReadOnlyDictionary<string, string> Fields,
    JsonElement? Condition);

public sealed record ScenarioTemplateDto(
    string WorkloadName,
    string ScenarioName,
    IReadOnlyList<ScenarioTemplateRuleDto> Rules);

/// <summary>Ergebnis eines Template-Imports: welches Szenario wurde angelegt/aktualisiert, welche Ressourcen neu angelegt, welche Regeln schlugen fehl.</summary>
public sealed record ScenarioImportResult(
    Guid? ScenarioId,
    IReadOnlyList<string> CreatedResourceNames,
    IReadOnlyList<string> Errors);

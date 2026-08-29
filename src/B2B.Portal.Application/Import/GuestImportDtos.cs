namespace B2B.Portal.Application.Import;

/// <summary>Zielschlüssel, auf die eine Excel-Spalte gemappt werden kann. Die vier Werte
/// sind reserviert (steuern die Gast-/Workload-/Szenario-Auflösung); jeder andere String in
/// ColumnToField.Values wird als freier ScenarioResourceRule.Fields-Schlüssel behandelt.</summary>
public static class GuestImportReservedFields
{
    public const string Mail = "Mail";
    public const string DisplayName = "DisplayName";
    public const string Workload = "Workload";
    public const string Szenario = "Szenario";

    public static readonly HashSet<string> All = [Mail, DisplayName, Workload, Szenario];
}

/// <summary>
/// Konfigurierbares Spalten-Mapping für einen Excel-Import (siehe GuestImportService).
/// SheetName/HeaderRowIndex/DataStartColumnIndex erlauben es, mit Dateien umzugehen, die
/// nicht bei Zeile 1/Spalte 1 beginnen (z. B. Titelzeile, Kommentarspalten links).
/// ColumnToField bildet den Spaltenindex (0-basiert, ab DataStartColumnIndex gezählt) auf
/// entweder einen reservierten Zielschlüssel (GuestImportReservedFields) oder einen freien
/// ScenarioResourceRule.Fields-Schlüssel ab.
/// </summary>
public sealed record GuestImportColumnMapping(
    string SheetName,
    int HeaderRowIndex,
    int DataStartColumnIndex,
    IReadOnlyDictionary<int, string> ColumnToField);

/// <summary>Ergebnis von POST /api/guest-import/inspect — die im gewählten Sheet gefundenen
/// Sheet-Namen (zur Auswahl) und die Kopfzeilen-Werte ab DataStartColumnIndex (zur
/// Mapping-Erstellung im UI).</summary>
public sealed record GuestImportInspectResult(
    IReadOnlyList<string> SheetNames,
    IReadOnlyList<string> ColumnHeaders);

/// <summary>Eine Warnung zu einer einzelnen Import-Zeile — blockiert den Commit nicht
/// (siehe Nutzerentscheidung: Gast wird trotzdem angelegt, Zeile bleibt in der Excel-Datei
/// korrigierbar für einen erneuten Lauf).</summary>
public sealed record GuestImportRowWarning(string Message);

/// <summary>Hinweis, dass eine bestehende Zuweisung des Gasts in einem ANDEREN Workload
/// durch die geänderten Zeilen-Daten fachlich zu prüfen sein könnte (führt beim Commit zu
/// einem ReviewItem mit Reason).</summary>
public sealed record GuestImportForeignWorkloadImpact(
    Guid WorkloadId, string WorkloadName, Guid AssignmentId, string Reason);

public sealed record GuestImportRowResult(
    int RowNumber,
    string Mail,
    string DisplayName,
    bool IsNewGuest,
    bool DataChanged,
    IReadOnlyList<string> MatchedRoleNames,
    IReadOnlyList<GuestImportRowWarning> Warnings,
    IReadOnlyList<GuestImportForeignWorkloadImpact> ForeignWorkloadImpacts);

public sealed record GuestImportResult(
    IReadOnlyList<GuestImportRowResult> Rows,
    int NewGuestCount,
    int UpdatedGuestCount,
    int AssignmentCount,
    int WarningCount);

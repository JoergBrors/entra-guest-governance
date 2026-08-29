namespace B2B.Portal.Domain.Services;

/// <summary>Eingabe für die Deletion-Gate-Prüfung eines einzelnen Gastes (Blueprint 14.2).</summary>
public sealed record DeletionGateInput(
    int ActiveWorkloadReferences,
    int UnclassifiedAccessCount,
    int OpenSecurityRelevantJobs,
    int OpenReviews,
    bool GracePeriodReached,
    bool ConnectorError,
    bool LiveCheckFoundRelevantAccess);

public enum DeletionGateResult
{
    Blocked,
    Ready
}

public sealed record DeletionGateEvaluation(DeletionGateResult Result, IReadOnlyList<string> Blockers);

/// <summary>
/// Reine Fachlogik des zentralen Deletion Gate. Kein Workload/Connector darf eine
/// Gastidentität direkt löschen (Anhang A, Regel 3) — dieser Evaluator ist die einzige
/// Stelle, die eine Löschung als "Ready" markieren darf, und selbst dann wird nie
/// automatisch gelöscht: die aufrufende Anwendungsschicht muss danach noch explizit
/// Disable -> Grace Period -> Delete auslösen.
///
/// Ein Connectorfehler wird konservativ behandelt: blockieren, nicht "kein Zugriff" annehmen
/// (Blueprint 14.4 "Live Check als letzte Instanz").
/// </summary>
public static class DeletionGateEvaluator
{
    public static DeletionGateEvaluation Evaluate(DeletionGateInput input)
    {
        var blockers = new List<string>();

        if (input.ActiveWorkloadReferences > 0)
        {
            blockers.Add($"ActiveWorkloadReferences={input.ActiveWorkloadReferences}");
        }

        if (input.UnclassifiedAccessCount > 0)
        {
            blockers.Add($"UnclassifiedAccess={input.UnclassifiedAccessCount}");
        }

        if (input.OpenSecurityRelevantJobs > 0)
        {
            blockers.Add($"OpenSecurityRelevantJobs={input.OpenSecurityRelevantJobs}");
        }

        if (input.OpenReviews > 0)
        {
            blockers.Add($"OpenReviews={input.OpenReviews}");
        }

        if (!input.GracePeriodReached)
        {
            blockers.Add("GracePeriodNotReached");
        }

        // Live Validation nur relevant, wenn die vorherigen Blocker bereits frei sind —
        // spiegelt das Flussdiagramm aus Blueprint 14.2 (Live Check ist die letzte Stufe).
        if (blockers.Count == 0)
        {
            if (input.ConnectorError)
            {
                blockers.Add("ConnectorError");
            }
            else if (input.LiveCheckFoundRelevantAccess)
            {
                blockers.Add("LiveCheckFoundRelevantAccess");
            }
        }

        return blockers.Count == 0
            ? new DeletionGateEvaluation(DeletionGateResult.Ready, blockers)
            : new DeletionGateEvaluation(DeletionGateResult.Blocked, blockers);
    }
}

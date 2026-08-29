using B2B.Portal.Domain.Services;
using Xunit;

namespace B2B.Portal.Domain.Tests;

/// <summary>
/// Testet die reine Fachlogik des Deletion Gate (Blueprint 14.2) — die zentrale
/// Sicherheitsinvariante des gesamten Systems. Deckt die im MVP-Verification-Prompt
/// geforderten Negativfälle ab: aktive Workload-Zuordnung, Unclassified Access,
/// offener Job, Connectorfehler, Live Check meldet Zugriff — sowie den Allow-Fall.
/// </summary>
public class DeletionGateEvaluatorTests
{
    private static DeletionGateInput ReadyInput() => new(
        ActiveWorkloadReferences: 0,
        UnclassifiedAccessCount: 0,
        OpenSecurityRelevantJobs: 0,
        OpenReviews: 0,
        GracePeriodReached: true,
        ConnectorError: false,
        LiveCheckFoundRelevantAccess: false);

    [Fact]
    public void Evaluate_AllClear_ReturnsReady()
    {
        var result = DeletionGateEvaluator.Evaluate(ReadyInput());

        Assert.Equal(DeletionGateResult.Ready, result.Result);
        Assert.Empty(result.Blockers);
    }

    [Fact]
    public void Evaluate_ActiveWorkloadReference_Blocks()
    {
        var input = ReadyInput() with { ActiveWorkloadReferences = 1 };
        var result = DeletionGateEvaluator.Evaluate(input);

        Assert.Equal(DeletionGateResult.Blocked, result.Result);
        Assert.Contains(result.Blockers, b => b.StartsWith("ActiveWorkloadReferences"));
    }

    [Fact]
    public void Evaluate_UnclassifiedAccess_Blocks()
    {
        var input = ReadyInput() with { UnclassifiedAccessCount = 2 };
        var result = DeletionGateEvaluator.Evaluate(input);

        Assert.Equal(DeletionGateResult.Blocked, result.Result);
        Assert.Contains(result.Blockers, b => b.StartsWith("UnclassifiedAccess"));
    }

    [Fact]
    public void Evaluate_OpenSecurityRelevantJob_Blocks()
    {
        var input = ReadyInput() with { OpenSecurityRelevantJobs = 1 };
        var result = DeletionGateEvaluator.Evaluate(input);

        Assert.Equal(DeletionGateResult.Blocked, result.Result);
        Assert.Contains(result.Blockers, b => b.StartsWith("OpenSecurityRelevantJobs"));
    }

    [Fact]
    public void Evaluate_OpenReview_Blocks()
    {
        var input = ReadyInput() with { OpenReviews = 1 };
        var result = DeletionGateEvaluator.Evaluate(input);

        Assert.Equal(DeletionGateResult.Blocked, result.Result);
        Assert.Contains(result.Blockers, b => b.StartsWith("OpenReviews"));
    }

    [Fact]
    public void Evaluate_GracePeriodNotReached_Blocks()
    {
        var input = ReadyInput() with { GracePeriodReached = false };
        var result = DeletionGateEvaluator.Evaluate(input);

        Assert.Equal(DeletionGateResult.Blocked, result.Result);
        Assert.Contains("GracePeriodNotReached", result.Blockers);
    }

    [Fact]
    public void Evaluate_ConnectorError_BlocksConservatively()
    {
        // Sicherheitsinvariante: ein Connectorfehler wird NIE als "kein Zugriff" gewertet.
        var input = ReadyInput() with { ConnectorError = true };
        var result = DeletionGateEvaluator.Evaluate(input);

        Assert.Equal(DeletionGateResult.Blocked, result.Result);
        Assert.Contains("ConnectorError", result.Blockers);
    }

    [Fact]
    public void Evaluate_LiveCheckFindsAccess_Blocks()
    {
        var input = ReadyInput() with { LiveCheckFoundRelevantAccess = true };
        var result = DeletionGateEvaluator.Evaluate(input);

        Assert.Equal(DeletionGateResult.Blocked, result.Result);
        Assert.Contains("LiveCheckFoundRelevantAccess", result.Blockers);
    }

    [Fact]
    public void Evaluate_MultipleBlockers_ReportsAll()
    {
        var input = ReadyInput() with { ActiveWorkloadReferences = 1, UnclassifiedAccessCount = 3 };
        var result = DeletionGateEvaluator.Evaluate(input);

        Assert.Equal(DeletionGateResult.Blocked, result.Result);
        Assert.Equal(2, result.Blockers.Count);
    }
}

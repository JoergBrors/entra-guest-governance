using System.Text.Json;
using B2B.Portal.Domain.Services;
using Xunit;

namespace B2B.Portal.Domain.Tests;

/// <summary>
/// Testet den minimalen JSONLogic-Auswerter (siehe JsonLogicEvaluator) — ein Test pro
/// unterstütztem Operator, plus verschachtelte Ausdrücke und die Validate-Vorabprüfung.
/// Folgt dem Muster von DeletionGateEvaluatorTests (typisierter Input, pure Funktion).
/// </summary>
public class JsonLogicEvaluatorTests
{
    private static ScenarioEvaluationContext DefaultContext(
        string guestAccountState = "Active",
        int activeAssignmentCount = 0,
        Dictionary<string, string>? fields = null,
        Dictionary<string, JsonElement>? additionalFacts = null) => new(
        guestAccountState, activeAssignmentCount,
        fields ?? new Dictionary<string, string>(),
        additionalFacts ?? new Dictionary<string, JsonElement>());

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Evaluate_Equals_MatchesFieldValue()
    {
        var expr = Parse("""{"==": [{"var": "Fields.Environment"}, "Test"]}""");

        Assert.True(JsonLogicEvaluator.Evaluate(expr, DefaultContext(fields: new() { ["Environment"] = "Test" })));
        Assert.False(JsonLogicEvaluator.Evaluate(expr, DefaultContext(fields: new() { ["Environment"] = "Prod" })));
    }

    [Fact]
    public void Evaluate_NotEquals_Works()
    {
        var expr = Parse("""{"!=": [{"var": "GuestAccountState"}, "Blocked"]}""");

        Assert.True(JsonLogicEvaluator.Evaluate(expr, DefaultContext(guestAccountState: "Active")));
        Assert.False(JsonLogicEvaluator.Evaluate(expr, DefaultContext(guestAccountState: "Blocked")));
    }

    [Fact]
    public void Evaluate_LessThan_ComparesNumbers()
    {
        var expr = Parse("""{"<": [{"var": "ActiveAssignmentCount"}, 5]}""");

        Assert.True(JsonLogicEvaluator.Evaluate(expr, DefaultContext(activeAssignmentCount: 2)));
        Assert.False(JsonLogicEvaluator.Evaluate(expr, DefaultContext(activeAssignmentCount: 10)));
    }

    [Fact]
    public void Evaluate_LessThanOrEqual_ComparesNumbers()
    {
        var expr = Parse("""{"<=": [{"var": "ActiveAssignmentCount"}, 5]}""");

        Assert.True(JsonLogicEvaluator.Evaluate(expr, DefaultContext(activeAssignmentCount: 5)));
        Assert.False(JsonLogicEvaluator.Evaluate(expr, DefaultContext(activeAssignmentCount: 6)));
    }

    [Fact]
    public void Evaluate_GreaterThan_ComparesNumbers()
    {
        var expr = Parse("""{">": [{"var": "ActiveAssignmentCount"}, 5]}""");

        Assert.True(JsonLogicEvaluator.Evaluate(expr, DefaultContext(activeAssignmentCount: 10)));
        Assert.False(JsonLogicEvaluator.Evaluate(expr, DefaultContext(activeAssignmentCount: 2)));
    }

    [Fact]
    public void Evaluate_GreaterThanOrEqual_ComparesNumbers()
    {
        var expr = Parse("""{">=": [{"var": "ActiveAssignmentCount"}, 5]}""");

        Assert.True(JsonLogicEvaluator.Evaluate(expr, DefaultContext(activeAssignmentCount: 5)));
        Assert.False(JsonLogicEvaluator.Evaluate(expr, DefaultContext(activeAssignmentCount: 4)));
    }

    [Fact]
    public void Evaluate_In_ChecksArrayMembership()
    {
        var expr = Parse("""{"in": [{"var": "GuestAccountState"}, ["Active", "Invited"]]}""");

        Assert.True(JsonLogicEvaluator.Evaluate(expr, DefaultContext(guestAccountState: "Active")));
        Assert.False(JsonLogicEvaluator.Evaluate(expr, DefaultContext(guestAccountState: "Blocked")));
    }

    [Fact]
    public void Evaluate_Not_NegatesInnerExpression()
    {
        var expr = Parse("""{"not": [{"==": [{"var": "Fields.Environment"}, "Prod"]}]}""");

        Assert.True(JsonLogicEvaluator.Evaluate(expr, DefaultContext(fields: new() { ["Environment"] = "Test" })));
        Assert.False(JsonLogicEvaluator.Evaluate(expr, DefaultContext(fields: new() { ["Environment"] = "Prod" })));
    }

    [Fact]
    public void Evaluate_And_RequiresAllTrue()
    {
        var expr = Parse("""
            {"and": [
                {"==": [{"var": "Fields.Environment"}, "Test"]},
                {"==": [{"var": "GuestAccountState"}, "Active"]}
            ]}
            """);

        Assert.True(JsonLogicEvaluator.Evaluate(
            expr, DefaultContext(guestAccountState: "Active", fields: new() { ["Environment"] = "Test" })));
        Assert.False(JsonLogicEvaluator.Evaluate(
            expr, DefaultContext(guestAccountState: "Blocked", fields: new() { ["Environment"] = "Test" })));
    }

    [Fact]
    public void Evaluate_Or_RequiresAnyTrue()
    {
        var expr = Parse("""
            {"or": [
                {"==": [{"var": "Fields.Environment"}, "Prod"]},
                {"==": [{"var": "GuestAccountState"}, "Active"]}
            ]}
            """);

        Assert.True(JsonLogicEvaluator.Evaluate(
            expr, DefaultContext(guestAccountState: "Active", fields: new() { ["Environment"] = "Test" })));
        Assert.False(JsonLogicEvaluator.Evaluate(
            expr, DefaultContext(guestAccountState: "Blocked", fields: new() { ["Environment"] = "Test" })));
    }

    [Fact]
    public void Evaluate_Var_ResolvesFields()
    {
        var expr = Parse("""{"var": "Fields.Rolle"}""");

        Assert.True(JsonLogicEvaluator.Evaluate(expr, DefaultContext(fields: new() { ["Rolle"] = "Disponent" })));
        Assert.False(JsonLogicEvaluator.Evaluate(expr, DefaultContext()));
    }

    [Fact]
    public void Evaluate_Var_ResolvesAdditionalFacts()
    {
        var facts = new Dictionary<string, JsonElement> { ["TestRunOccurred"] = Parse("true") };
        var expr = Parse("""{"var": "AdditionalFacts.TestRunOccurred"}""");

        Assert.True(JsonLogicEvaluator.Evaluate(expr, DefaultContext(additionalFacts: facts)));
    }

    [Fact]
    public void Evaluate_NestedAndOrNot_ReflectsComplexCondition()
    {
        // "wenn Rolle=Disponent UND (Fields.Environment=Test ODER nicht Blocked)"
        var expr = Parse("""
            {"and": [
                {"==": [{"var": "Fields.Rolle"}, "Disponent"]},
                {"or": [
                    {"==": [{"var": "Fields.Environment"}, "Test"]},
                    {"not": [{"==": [{"var": "GuestAccountState"}, "Blocked"]}]}
                ]}
            ]}
            """);
        var fields = new Dictionary<string, string> { ["Rolle"] = "Disponent", ["Environment"] = "Prod" };

        // Rolle passt, Environment=Prod, aber Gast nicht Blocked -> "or"-Zweig via "not" true.
        Assert.True(JsonLogicEvaluator.Evaluate(
            expr, DefaultContext(guestAccountState: "Active", fields: fields)));

        // Rolle passt, Environment=Prod UND Gast Blocked -> beide "or"-Zweige false.
        Assert.False(JsonLogicEvaluator.Evaluate(
            expr, DefaultContext(guestAccountState: "Blocked", fields: fields)));
    }

    [Fact]
    public void Evaluate_UnsupportedOperator_Throws()
    {
        var expr = Parse("""{"map": [{"var": "x"}, {"+": [1, 2]}]}""");

        Assert.Throws<NotSupportedException>(() => JsonLogicEvaluator.Evaluate(expr, DefaultContext()));
    }

    [Fact]
    public void Validate_SupportedExpression_DoesNotThrow()
    {
        var expr = Parse("""{"and": [{"==": [{"var": "Fields.Environment"}, "Test"]}, {"var": "AdditionalFacts.X"}]}""");

        var exception = Record.Exception(() => JsonLogicEvaluator.Validate(expr));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_UnsupportedOperator_ThrowsWithoutContext()
    {
        var expr = Parse("""{"reduce": [{"var": "x"}, {}, 0]}""");

        Assert.Throws<NotSupportedException>(() => JsonLogicEvaluator.Validate(expr));
    }
}

using System.Text.Json;

namespace B2B.Portal.Domain.Services;

/// <summary>
/// Typisierte Fakten, gegen die die Condition einer ScenarioResourceRule ausgewertet wird —
/// "gather facts, then evaluate pure function", dasselbe Muster wie
/// DeletionGateInput/DeletionGateEvaluator. Fields spiegelt genau die frei definierten
/// Schlüssel der jeweiligen Regel (siehe ScenarioResourceRule.Fields, z. B. "Firma",
/// "Rolle", "Environment") — das "var"-Lookup liest "Fields.X" daraus.
/// AdditionalFacts deckt darüber hinausgehende Felder ab, die nicht Teil der Regel selbst
/// sind (z. B. "TestRunOccurred" aus einem Job-Payload).
/// </summary>
public sealed record ScenarioEvaluationContext(
    string GuestAccountState,
    int ActiveAssignmentCount,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyDictionary<string, JsonElement> AdditionalFacts);

/// <summary>
/// Minimaler, selbst implementierter JSONLogic-Auswerter (bewusst kein NuGet-Paket — Domain
/// referenziert konsequent keine Pakete, siehe DomainIsolationTests, und die
/// .NET-JSONLogic-Paketlandschaft ist zu dünn für eine verlässliche Empfehlung ohne
/// Lizenz-/Supply-Chain-Risiko). Unterstützter Operator-Satz v1: and, or, not, ==, !=, &lt;,
/// &lt;=, &gt;, &gt;=, in, var — reicht für "beliebig verschachtelte Boolean-Ausdrücke" ohne
/// JSONLogics exotischere Operatoren (map/reduce/...). Nicht unterstützte Operatoren werfen
/// bewusst statt eine Bedingung, die nie zutrifft ("fail loud" für Szenario-Autoren).
///
/// Format folgt der JSONLogic-Konvention: {"operator": [arg1, arg2, ...]},
/// {"var": "FieldName"} für einen Fakten-Zugriff, Literale als JSON-Werte.
/// Beispiel: {"and": [{"==": [{"var":"Fields.Environment"}, "Test"]}, {"var":"AdditionalFacts.TestRunOccurred"}]}
/// </summary>
public static class JsonLogicEvaluator
{
    private static readonly HashSet<string> SupportedOperators =
        ["and", "or", "not", "==", "!=", "<", "<=", ">", ">=", "in", "var"];

    public static bool Evaluate(JsonElement expression, ScenarioEvaluationContext context) =>
        ToBool(EvaluateNode(expression, context));

    /// <summary>
    /// Prüft, ob der Ausdruck ausschließlich unterstützte Operatoren verwendet — ohne einen
    /// echten Kontext zu benötigen. Für Import-/Editor-Zeit-Validierung (siehe
    /// ScenarioImportExportService), damit ein Szenario-Autor sofortiges Feedback bekommt,
    /// statt eine nie zutreffende Bedingung erst beim Deploy zu entdecken.
    /// </summary>
    public static void Validate(JsonElement expression)
    {
        if (expression.ValueKind != JsonValueKind.Object)
        {
            return; // Literal (Zahl/String/Bool/Array) — immer gültig als Teilausdruck.
        }

        foreach (var property in expression.EnumerateObject())
        {
            if (!SupportedOperators.Contains(property.Name))
            {
                throw new NotSupportedException(
                    $"JSONLogic-Operator '{property.Name}' wird nicht unterstützt. " +
                    $"Unterstützt: {string.Join(", ", SupportedOperators)}.");
            }

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var arg in property.Value.EnumerateArray())
                {
                    Validate(arg);
                }
            }
            else
            {
                Validate(property.Value);
            }
        }
    }

    private static object? EvaluateNode(JsonElement node, ScenarioEvaluationContext context)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                var property = node.EnumerateObject().FirstOrDefault();
                if (property.Value.ValueKind == JsonValueKind.Undefined)
                {
                    throw new NotSupportedException("Leeres JSONLogic-Objekt ist kein gültiger Ausdruck.");
                }
                return EvaluateOperator(property.Name, property.Value, context);

            case JsonValueKind.String:
                return node.GetString();
            case JsonValueKind.Number:
                return node.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.Array:
                return node.EnumerateArray().Select(e => EvaluateNode(e, context)).ToList();
            default:
                throw new NotSupportedException($"JSONLogic-Knoten vom Typ {node.ValueKind} wird nicht unterstützt.");
        }
    }

    private static object? EvaluateOperator(string op, JsonElement argsElement, ScenarioEvaluationContext context)
    {
        if (!SupportedOperators.Contains(op))
        {
            throw new NotSupportedException(
                $"JSONLogic-Operator '{op}' wird nicht unterstützt. " +
                $"Unterstützt: {string.Join(", ", SupportedOperators)}.");
        }

        if (op == "var")
        {
            var path = argsElement.ValueKind == JsonValueKind.Array
                ? argsElement.EnumerateArray().First().GetString()!
                : argsElement.GetString()!;
            return ResolveVar(path, context);
        }

        var args = argsElement.ValueKind == JsonValueKind.Array
            ? argsElement.EnumerateArray().Select(e => EvaluateNode(e, context)).ToList()
            : [EvaluateNode(argsElement, context)];

        return op switch
        {
            "and" => args.All(ToBool),
            "or" => args.Any(ToBool),
            "not" => !ToBool(args.Single()),
            "==" => AreEqual(args[0], args[1]),
            "!=" => !AreEqual(args[0], args[1]),
            "<" => Compare(args[0], args[1]) < 0,
            "<=" => Compare(args[0], args[1]) <= 0,
            ">" => Compare(args[0], args[1]) > 0,
            ">=" => Compare(args[0], args[1]) >= 0,
            "in" => Contains(args[1], args[0]),
            _ => throw new NotSupportedException($"JSONLogic-Operator '{op}' wird nicht unterstützt."),
        };
    }

    private static object? ResolveVar(string path, ScenarioEvaluationContext context)
    {
        // Bekannte feste Fakten zuerst (geschlossene, auditierbare Fakten-Oberfläche statt
        // Reflection), danach die dotted-path-Fallbacks "Fields.X" (die frei definierten
        // Schlüssel der auswertenden Regel, z. B. Firma/Rolle) und "AdditionalFacts.X"
        // (Fakten außerhalb der Regel selbst, z. B. aus einem Job-Payload).
        object? direct = path switch
        {
            "GuestAccountState" => context.GuestAccountState,
            "ActiveAssignmentCount" => (double)context.ActiveAssignmentCount,
            _ => null,
        };
        if (direct is not null)
        {
            return direct;
        }

        const string fieldsPrefix = "Fields.";
        if (path.StartsWith(fieldsPrefix, StringComparison.Ordinal))
        {
            var key = path[fieldsPrefix.Length..];
            return context.Fields.TryGetValue(key, out var value) ? value : null;
        }

        const string factsPrefix = "AdditionalFacts.";
        if (path.StartsWith(factsPrefix, StringComparison.Ordinal))
        {
            var key = path[factsPrefix.Length..];
            if (context.AdditionalFacts.TryGetValue(key, out var fact))
            {
                return EvaluateNode(fact, context);
            }
        }

        return null;
    }

    private static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        double d => d != 0,
        string s => s.Length > 0,
        List<object?> list => list.Count > 0,
        _ => true,
    };

    private static bool AreEqual(object? a, object? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (a is double da && b is double db)
        {
            return Math.Abs(da - db) < double.Epsilon;
        }

        return string.Equals(Convert.ToString(a), Convert.ToString(b), StringComparison.Ordinal);
    }

    private static int Compare(object? a, object? b)
    {
        if (a is double da && b is double db)
        {
            return da.CompareTo(db);
        }

        return string.CompareOrdinal(Convert.ToString(a), Convert.ToString(b));
    }

    private static bool Contains(object? haystack, object? needle) => haystack switch
    {
        List<object?> list => list.Any(x => AreEqual(x, needle)),
        string s when needle is string sub => s.Contains(sub, StringComparison.Ordinal),
        _ => false,
    };
}

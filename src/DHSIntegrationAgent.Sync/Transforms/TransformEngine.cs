using System.Text.Json.Nodes;
using DHSIntegrationAgent.Sync.Descriptor;

namespace DHSIntegrationAgent.Sync.Transforms;

public sealed class TransformEngine
{
    private readonly Dictionary<string, ITransformStep> _registry = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ITransformStep step)
    {
        _registry[step.Name] = step;
    }

    public void ApplyTransforms(JsonObject row, List<TransformRule> rules)
    {
        if (rules == null || !rules.Any()) return;

        foreach (var rule in rules)
        {
            var colName = rule.Column;

            // Case-insensitive lookup to find the actual key in the JsonObject
            var actualKey = row.Select(x => x.Key).FirstOrDefault(k => string.Equals(k, colName, StringComparison.OrdinalIgnoreCase));
            if (actualKey == null) continue; // Column not found in this row

            foreach (var stepDef in rule.Steps)
            {
                if (_registry.TryGetValue(stepDef.Name, out var stepImpl))
                {
                    stepImpl.Apply(row, actualKey, stepDef.Parameters);
                }
            }

            // Rename to canonical if needed (ensure strict casing for CanonicalBuilder)
            if (actualKey != colName)
            {
                var val = row[actualKey];
                row.Remove(actualKey);

                // Deep clone the node if it's not null to avoid "node already has a parent" exception
                if (val != null)
                {
                   row[colName] = JsonNode.Parse(val.ToJsonString());
                }
                else
                {
                   row[colName] = null;
                }
            }
        }
    }
}

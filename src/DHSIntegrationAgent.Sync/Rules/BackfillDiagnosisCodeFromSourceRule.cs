using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;

namespace DHSIntegrationAgent.Sync.Rules;

public sealed class BackfillDiagnosisCodeFromSourceRule : IPostLoadRule
{
    public string RuleName => "BackfillDiagnosisCodeFromSource";

    public void Apply(ProviderClaimBundleRaw bundle, JsonObject ruleParams)
    {
        var sourceColumn = ruleParams["sourceColumn"]?.GetValue<string>();
        var targetColumn = ruleParams["targetColumn"]?.GetValue<string>();
        if (sourceColumn is null || targetColumn is null) return;

        if (!bundle.Header.TryGetPropertyValue(sourceColumn, out var primary) || primary is null) return;

        foreach (var item in bundle.Diagnoses.OfType<JsonObject>())
        {
            if (!item.ContainsKey(targetColumn) || item[targetColumn] is null)
                item[targetColumn] = primary.DeepClone();
        }
    }
}

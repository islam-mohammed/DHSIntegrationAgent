using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;

namespace DHSIntegrationAgent.Sync.Rules;

public sealed class ClampHeaderFieldRule : IPostLoadRule
{
    public string RuleName => "ClampHeaderField";

    public void Apply(ProviderClaimBundleRaw bundle, JsonObject ruleParams)
    {
        var column = ruleParams["column"]?.GetValue<string>();
        if (column is null) return;

        var min = ruleParams["min"]?.GetValue<int>();
        var max = ruleParams["max"]?.GetValue<int>();

        if (!bundle.Header.TryGetPropertyValue(column, out var node) || node is null) return;

        if (!int.TryParse(node.ToString(), out var val)) return;

        if ((min.HasValue && val < min.Value) || (max.HasValue && val > max.Value))
            bundle.Header[column] = null;
    }
}

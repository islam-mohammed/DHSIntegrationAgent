using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;

namespace DHSIntegrationAgent.Sync.Rules;

public sealed class ForceServiceFieldRule : IPostLoadRule
{
    public string RuleName => "ForceServiceField";

    public void Apply(ProviderClaimBundleRaw bundle, JsonObject ruleParams)
    {
        var column = ruleParams["column"]?.GetValue<string>();
        var value  = ruleParams["value"];
        if (column is null) return;

        foreach (var item in bundle.Services.OfType<JsonObject>())
            item[column] = value?.DeepClone();
    }
}

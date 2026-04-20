using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;

namespace DHSIntegrationAgent.Sync.Rules;

public sealed class SetInvestigationResultRule : IPostLoadRule
{
    public string RuleName => "SetInvestigationResult";

    public void Apply(ProviderClaimBundleRaw bundle, JsonObject ruleParams)
    {
        var resultColumn = ruleParams["resultColumn"]?.GetValue<string>();
        if (resultColumn is null) return;

        foreach (var item in bundle.Labs.OfType<JsonObject>())
        {
            if (!item.ContainsKey(resultColumn))
                item[resultColumn] = "N";
        }
    }
}

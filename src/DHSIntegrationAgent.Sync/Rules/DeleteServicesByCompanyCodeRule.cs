using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;

namespace DHSIntegrationAgent.Sync.Rules;

public sealed class DeleteServicesByCompanyCodeRule : IPostLoadRule
{
    public string RuleName => "DeleteServicesByCompanyCode";

    public void Apply(ProviderClaimBundleRaw bundle, JsonObject ruleParams)
    {
        var codes = ruleParams["companyCodes"]?.AsArray()
            .Select(n => n?.GetValue<string>())
            .Where(s => s is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (codes is null || codes.Count == 0) return;

        var zeroOnly = ruleParams["zeroAmountOnly"]?.GetValue<bool>() ?? false;

        for (int i = bundle.Services.Count - 1; i >= 0; i--)
        {
            if (bundle.Services[i] is not JsonObject row) continue;

            var cc = row["CompanyCode"]?.GetValue<string>();
            if (cc is null || !codes.Contains(cc)) continue;

            if (zeroOnly)
            {
                var net = row["NetAmount"];
                if (net is not null && net.GetValue<decimal>() != 0m) continue;
            }

            bundle.Services.RemoveAt(i);
        }
    }
}

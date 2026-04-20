using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;

namespace DHSIntegrationAgent.Sync.Rules;

public sealed class RecomputeHeaderTotalsRule : IPostLoadRule
{
    public string RuleName => "RecomputeHeaderTotals";

    public void Apply(ProviderClaimBundleRaw bundle, JsonObject ruleParams)
    {
        var netCol      = ruleParams["netAmountColumn"]?.GetValue<string>();
        var claimedCol  = ruleParams["claimedColumn"]?.GetValue<string>();
        var discountCol = ruleParams["discountColumn"]?.GetValue<string>();
        var deductCol   = ruleParams["deductibleColumn"]?.GetValue<string>();
        var svcNetCol   = ruleParams["serviceNetColumn"]?.GetValue<string>();
        var svcClaimedCol = ruleParams["serviceClaimedColumn"]?.GetValue<string>();

        decimal sumNet     = 0m;
        decimal sumClaimed = 0m;
        decimal sumDisc    = 0m;
        decimal sumDeduct  = 0m;

        foreach (var item in bundle.Services.OfType<JsonObject>())
        {
            sumNet     += GetDecimal(item, svcNetCol);
            sumClaimed += GetDecimal(item, svcClaimedCol);
        }

        if (netCol is not null)      bundle.Header[netCol]      = sumNet;
        if (claimedCol is not null)  bundle.Header[claimedCol]  = sumClaimed;
        if (discountCol is not null) bundle.Header[discountCol] = sumDisc;
        if (deductCol is not null)   bundle.Header[deductCol]   = sumDeduct;
    }

    private static decimal GetDecimal(JsonObject obj, string? column)
    {
        if (column is null) return 0m;
        if (!obj.TryGetPropertyValue(column, out var node) || node is null) return 0m;
        try { return node.GetValue<decimal>(); } catch { return 0m; }
    }
}

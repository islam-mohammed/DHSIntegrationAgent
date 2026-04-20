using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;

namespace DHSIntegrationAgent.Sync.Rules;

public interface IPostLoadRule
{
    string RuleName { get; }
    void Apply(ProviderClaimBundleRaw bundle, JsonObject ruleParams);
}

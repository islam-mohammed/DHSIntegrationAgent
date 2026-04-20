using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.Sync.Pipeline;

namespace DHSIntegrationAgent.Sync.Rules;

public sealed class PostLoadRuleEngine
{
    private readonly IReadOnlyDictionary<string, IPostLoadRule> _rules;

    public PostLoadRuleEngine(IEnumerable<IPostLoadRule> rules)
    {
        _rules = rules.ToDictionary(r => r.RuleName, StringComparer.OrdinalIgnoreCase);
    }

    public ProviderClaimBundleRaw Apply(ProviderClaimBundleRaw bundle, IReadOnlyList<PostLoadRuleConfig> ruleConfigs)
    {
        foreach (var config in ruleConfigs)
        {
            if (_rules.TryGetValue(config.Rule, out var rule))
                rule.Apply(bundle, config.Params ?? new JsonObject());
        }
        return bundle;
    }
}

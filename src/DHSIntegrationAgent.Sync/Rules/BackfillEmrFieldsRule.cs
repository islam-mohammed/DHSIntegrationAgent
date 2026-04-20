using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;

namespace DHSIntegrationAgent.Sync.Rules;

public sealed class BackfillEmrFieldsRule : IPostLoadRule
{
    public string RuleName => "BackfillEmrFields";

    public void Apply(ProviderClaimBundleRaw bundle, JsonObject ruleParams)
    {
        var fields = ruleParams["fields"]?.AsArray()
            .Select(n => n?.GetValue<string>())
            .Where(s => s is not null)
            .ToList();

        if (fields is null || fields.Count == 0) return;

        foreach (var item in bundle.Services.OfType<JsonObject>())
        {
            foreach (var field in fields)
            {
                if (!item.ContainsKey(field!) && bundle.Header.TryGetPropertyValue(field!, out var val))
                    item[field!] = val?.DeepClone();
            }
        }
    }
}

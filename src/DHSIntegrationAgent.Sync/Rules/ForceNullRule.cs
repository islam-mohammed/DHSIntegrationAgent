using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;

namespace DHSIntegrationAgent.Sync.Rules;

public sealed class ForceNullRule : IPostLoadRule
{
    public string RuleName => "ForceNull";

    public void Apply(ProviderClaimBundleRaw bundle, JsonObject ruleParams)
    {
        var entity = ruleParams["entity"]?.GetValue<string>();
        var column = ruleParams["column"]?.GetValue<string>();
        if (entity is null || column is null) return;

        if (string.Equals(entity, "header", StringComparison.OrdinalIgnoreCase))
        {
            bundle.Header[column] = null;
            return;
        }

        var arr = EntityArray(bundle, entity);
        if (arr is null) return;

        foreach (var item in arr.OfType<JsonObject>())
            item[column] = null;
    }

    private static JsonArray? EntityArray(ProviderClaimBundleRaw b, string entity)
        => entity.ToLowerInvariant() switch
        {
            "service"   or "servicedetails"   => b.Services,
            "diagnosis" or "diagnosisdetails" => b.Diagnoses,
            "lab"       or "labdetails"       => b.Labs,
            "radiology" or "radiologydetails" => b.Radiology,
            "doctor"    or "dhsdoctors"       => b.DhsDoctors,
            "attachment"                      => b.Attachments,
            _ => null
        };
}

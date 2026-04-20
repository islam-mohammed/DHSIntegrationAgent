using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;

namespace DHSIntegrationAgent.Sync.Rules;

public sealed class NullIfColumnEqualsRule : IPostLoadRule
{
    public string RuleName => "NullIfColumnEquals";

    public void Apply(ProviderClaimBundleRaw bundle, JsonObject ruleParams)
    {
        var entity  = ruleParams["entity"]?.GetValue<string>();
        var column  = ruleParams["column"]?.GetValue<string>();
        var ifValue = ruleParams["ifValue"]?.GetValue<string>();

        if (entity is null || column is null || ifValue is null) return;

        if (string.Equals(entity, "header", StringComparison.OrdinalIgnoreCase))
        {
            NullifyIfMatch(bundle.Header, column, ifValue);
            return;
        }

        var arr = EntityArray(bundle, entity);
        if (arr is null) return;

        foreach (var item in arr.OfType<JsonObject>())
            NullifyIfMatch(item, column, ifValue);
    }

    private static void NullifyIfMatch(JsonObject obj, string column, string ifValue)
    {
        if (obj.TryGetPropertyValue(column, out var node) &&
            node?.GetValue<string>() == ifValue)
        {
            obj[column] = null;
        }
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

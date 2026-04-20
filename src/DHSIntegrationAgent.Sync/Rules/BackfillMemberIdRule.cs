using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;

namespace DHSIntegrationAgent.Sync.Rules;

public sealed class BackfillMemberIdRule : IPostLoadRule
{
    public string RuleName => "BackfillMemberId";

    public void Apply(ProviderClaimBundleRaw bundle, JsonObject ruleParams)
    {
        var memberIdColumn  = ruleParams["memberIdColumn"]?.GetValue<string>() ?? "MemberID";
        var patientIdColumn = ruleParams["patientIdColumn"]?.GetValue<string>() ?? "PatientID";

        if (!bundle.Header.TryGetPropertyValue(memberIdColumn, out var memberId) || memberId is null)
        {
            if (bundle.Header.TryGetPropertyValue(patientIdColumn, out var patientId) && patientId is not null)
                bundle.Header[memberIdColumn] = patientId.DeepClone();
        }
    }
}

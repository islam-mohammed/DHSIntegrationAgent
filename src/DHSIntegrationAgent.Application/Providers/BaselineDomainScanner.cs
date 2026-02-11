using System.Text.Json.Nodes;
using DHSIntegrationAgent.Domain.Claims;

namespace DHSIntegrationAgent.Application.Providers;

public sealed record BaselineDomain(string DomainName, int DomainTableId, string FieldPath);

public sealed record ScannedDomainValue(string DomainName, int DomainTableId, string Value);

public sealed class BaselineDomainScanner
{
    public static IReadOnlyList<BaselineDomain> GetBaselineDomains() => new List<BaselineDomain>
    {
        new("Gender", 12, "claimHeader.patientGender"),
        new("ClaimType", 1, "claimHeader.claimType"),
        new("VisitType", 2, "claimHeader.visitType"),
        new("SubscriberRelationship", 14, "claimHeader.subscriberRelationship"),
        new("MaritalStatus", 3, "claimHeader.maritalStatus"),
        new("Nationality", 4, "claimHeader.nationality"),
        new("PatientIdType", 5, "claimHeader.patientIdType"),
        new("AdmissionType", 6, "claimHeader.admissionType"),
        new("EncounterStatus", 7, "claimHeader.encounterStatus"),
        new("TriageCategory", 8, "claimHeader.triageCategoryTypeID"),
        new("ServiceType", 9, "serviceDetails[].serviceType"),
        new("ServiceEventType", 10, "serviceDetails[].serviceEventType"),
        new("PharmacistSelectionReason", 11, "serviceDetails[].pharmacistSelectionReason"),
        new("PharmacistSubstitute", 13, "serviceDetails[].pharmacistSubstitute"),
        new("DiagnosisType", 15, "diagnosisDetails[].diagnosisType"),
        new("ConditionOnset", 16, "diagnosisDetails[].onsetConditionTypeID")
    };

    public IReadOnlyList<ScannedDomainValue> Scan(ClaimBundle bundle)
    {
        var results = new List<ScannedDomainValue>();
        var domains = GetBaselineDomains();

        foreach (var domain in domains)
        {
            var values = ExtractValues(bundle, domain.FieldPath);
            foreach (var val in values)
            {
                if (!string.IsNullOrWhiteSpace(val))
                {
                    results.Add(new ScannedDomainValue(domain.DomainName, domain.DomainTableId, val.Trim()));
                }
            }
        }

        return results.Distinct().ToList();
    }

    private IEnumerable<string> ExtractValues(ClaimBundle bundle, string fieldPath)
    {
        if (fieldPath.StartsWith("claimHeader."))
        {
            var propName = fieldPath.Substring("claimHeader.".Length);
            return ExtractFromObject(bundle.ClaimHeader, propName);
        }

        if (fieldPath.StartsWith("serviceDetails[]."))
        {
            var propName = fieldPath.Substring("serviceDetails[].".Length);
            return ExtractFromArray(bundle.ServiceDetails, propName);
        }

        if (fieldPath.StartsWith("diagnosisDetails[]."))
        {
            var propName = fieldPath.Substring("diagnosisDetails[].".Length);
            return ExtractFromArray(bundle.DiagnosisDetails, propName);
        }

        return Enumerable.Empty<string>();
    }

    private IEnumerable<string> ExtractFromObject(JsonObject obj, string propName)
    {
        if (obj.TryGetPropertyValue(propName, out var node) && node is JsonValue val)
        {
            var s = val.ToString();
            if (!string.IsNullOrWhiteSpace(s)) yield return s;
        }
    }

    private IEnumerable<string> ExtractFromArray(JsonArray arr, string propName)
    {
        foreach (var item in arr)
        {
            if (item is JsonObject obj)
            {
                foreach (var val in ExtractFromObject(obj, propName))
                {
                    yield return val;
                }
            }
        }
    }
}

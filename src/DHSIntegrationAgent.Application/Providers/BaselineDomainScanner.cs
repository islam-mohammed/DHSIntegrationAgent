using System.Text.Json.Nodes;
using DHSIntegrationAgent.Domain.Claims;

namespace DHSIntegrationAgent.Application.Providers;

public sealed record BaselineDomain(string DomainName, int DomainTableId, string FieldPath);

public sealed record ScannedDomainValue(string DomainName, int DomainTableId, string Value);

public static class BaselineDomainScanner
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
}

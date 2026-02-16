using System.Text.Json.Nodes;
using DHSIntegrationAgent.Domain.Claims;

namespace DHSIntegrationAgent.Application.Providers;

public sealed record BaselineDomain(string DomainName, int DomainTableId, string FieldPath);

public sealed record ScannedDomainValue(string DomainName, int DomainTableId, string Value);

public static class BaselineDomainScanner
{
    public static IReadOnlyList<BaselineDomain> GetBaselineDomains() => new List<BaselineDomain>
    {
        new("PatientGender", 12, "claimHeader.patientGender"),
        new("DoctorGender", 12, "claimHeader.doctorGender"),
        new("ActIncidentCode", 28, "claimHeader.actIncidentCode"),
        new("ClaimType", 37, "claimHeader.claimType"),
        new("VisitType", 27, "claimHeader.visitType"),
        new("SubscriberRelationship", 55, "claimHeader.subscriberRelationship"),
        new("MaritalStatus", 15, "claimHeader.maritalStatus"),
        new("Country", 5, "claimHeader.nationality"),
        new("PatientIDType", 17, "claimHeader.patientIdType"),
        new("EncounterStatus", 47, "claimHeader.encounterStatus"),
        new("CareType", 33, "claimHeader.careTypeID"),
        new("EncounterType", 10, "claimHeader.enconuterTypeID"),
        new("Religion", 24, "claimHeader.patientReligion"),
        new("AdmissionType", 30, "claimHeader.admissionTypeID"),
        new("TriageCategory", 59, "claimHeader.triageCategoryTypeID"),
        new("Department", 62, "claimHeader.BenHead"),
        new("DischargeDisposition", 41, "claimHeader.DischargeDepositionsTypeID"),
        new("EncounterClass", 45, "claimHeader.enconuterTypeId"),
        new("EmergencyArrivalCode", 73, "claimHeader.emergencyArrivalCode"),
        new("DispositionCode", 43, "claimHeader.EmergencyDepositionTypeID"),
        new("InvestigationResult", 76, "claimHeader.investigationResult"),
        new("PatientOccupation", 19, "claimHeader.patientOccupation"),
        new("Country", 5, "claimHeader.Nationality"),
        new("SubmissionReasonCode", 25, "serviceDetails.submissionReasonCode"),
        new("TreatmentTypeIndicator", 26, "serviceDetails.treatmentTypeIndicator"),        
        new("ServiceType", 65, "serviceDetails.serviceType"),
        new("ServiceEventType", 72, "serviceDetails.serviceEventType"),
        new("Pharmacist Selection Reason", 77, "serviceDetails.pharmacistSelectionReason"),
        new("Pharmacist Substitute", 78, "serviceDetails.pharmacistSubstitute"),
        new("DiagnosisType", 7, "diagnosisDetails.diagnosisTypeID"),
        new("ExtendedDiagnosisTypeIndicator", 11, "diagnosisDetails.diagnosisType"),
        new("IllnessTypeIndicator", 14, "diagnosisDetails.illnessTypeIndicator"),
        new("ConditionOnset", 75, "diagnosisDetails.onsetConditionTypeID"),
        new("DoctorReligion", 24, "doctorDetails.religion_Code"),
    };
}

using System.Text.Json.Nodes;
using DHSIntegrationAgent.Domain.Claims;

namespace DHSIntegrationAgent.Application.Providers;

public sealed record BaselineDomain(string DomainName, DomainTableId DomainTableId, string FieldPath);

public sealed record ScannedDomainValue(string DomainName, DomainTableId DomainTableId, string Value);

public static class BaselineDomainScanner
{
    public static IReadOnlyList<BaselineDomain> GetBaselineDomains() => new List<BaselineDomain>
    {
        new("PatientGender", DomainTableId.Gender, "claimHeader.patientGender"),
        new("DoctorGender", DomainTableId.Gender, "claimHeader.doctorGender"),
        new("ActIncidentCode", DomainTableId.ActIncidentCode, "claimHeader.actIncidentCode"),
        new("ClaimType", DomainTableId.ClaimType, "claimHeader.claimType"),
        new("VisitType", DomainTableId.VisitType, "claimHeader.visitType"),
        new("SubscriberRelationship", DomainTableId.SubscriberRelationship, "claimHeader.subscriberRelationship"),
        new("MaritalStatus", DomainTableId.MaritalStatus, "claimHeader.maritalStatus"),
        new("PatientCountry", DomainTableId.Country, "claimHeader.nationality"),
        new("DoctorCountry", DomainTableId.Country, "claimHeader.Nationality_Code"),
        new("PatientIDType", DomainTableId.PatientIdType, "claimHeader.patientIdType"),
        new("EncounterStatus", DomainTableId.EncounterStatus, "claimHeader.encounterStatus"),
        new("CareType", DomainTableId.CareType, "claimHeader.careTypeID"),
        new("EncounterType", DomainTableId.EncounterType, "claimHeader.enconuterTypeID"),
        new("Religion", DomainTableId.Religion, "claimHeader.patientReligion"),
        new("AdmissionType", DomainTableId.AdmissionType, "claimHeader.admissionTypeID"),
        new("TriageCategory", DomainTableId.TriageCategory, "claimHeader.triageCategoryTypeID"),
        new("Department", DomainTableId.Department, "claimHeader.BenHead"),
        new("DischargeDisposition", DomainTableId.DischargeDisposition, "claimHeader.DischargeDepositionsTypeID"),
        new("EncounterClass", DomainTableId.EncounterClass, "claimHeader.enconuterTypeId"),
        new("EmergencyArrivalCode", DomainTableId.EmergencyArrivalCode, "claimHeader.emergencyArrivalCode"),
        new("DispositionCode", DomainTableId.DispositionCode, "claimHeader.EmergencyDepositionTypeID"),
        new("InvestigationResult", DomainTableId.InvestigationResult, "claimHeader.investigationResult"),
        new("PatientOccupation", DomainTableId.PatientOccupation, "claimHeader.patientOccupation"),
        new("Country", DomainTableId.Country, "claimHeader.Nationality"),
        new("SubmissionReasonCode", DomainTableId.SubmissionReasonCode, "serviceDetails.submissionReasonCode"),
        new("TreatmentTypeIndicator", DomainTableId.TreatmentTypeIndicator, "serviceDetails.treatmentTypeIndicator"),
        new("ServiceType", DomainTableId.ServiceType, "serviceDetails.serviceType"),
        new("ServiceEventType", DomainTableId.ServiceEventType, "serviceDetails.serviceEventType"),
        new("Pharmacist Selection Reason", DomainTableId.PharmacistSelectionReason, "serviceDetails.pharmacistSelectionReason"),
        new("Pharmacist Substitute", DomainTableId.PharmacistSubstitute, "serviceDetails.pharmacistSubstitute"),
        new("DiagnosisType", DomainTableId.DiagnosisType, "diagnosisDetails.diagnosisTypeID"),
        new("ExtendedDiagnosisTypeIndicator", DomainTableId.ExtendedDiagnosisTypeIndicator, "diagnosisDetails.DiagnosisTypeID"),
        new("IllnessTypeIndicator", DomainTableId.IllnessTypeIndicator, "diagnosisDetails.illnessTypeIndicator"),
        new("ConditionOnset", DomainTableId.ConditionOnset, "diagnosisDetails.onsetConditionTypeID"),
        new("DoctorReligion", DomainTableId.Religion, "dhsDoctors.religion_Code"),
        new("DoctorType", DomainTableId.DoctorType, "dhsDoctors.DoctorType_Code"),
    };
}

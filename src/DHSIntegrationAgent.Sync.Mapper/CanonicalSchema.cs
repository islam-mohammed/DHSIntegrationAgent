namespace DHSIntegrationAgent.Sync.Mapper;

public static class CanonicalSchema
{
    public const string ProIdClaim = "ProIdClaim";
    public const string CompanyCode = "CompanyCode";
    public const string InvoiceDate = "InvoiceDate";

    // Entity keys (match descriptor "sources" and "columnManifests" keys)
    public const string Header     = "header";
    public const string Doctor     = "doctor";
    public const string Service    = "service";
    public const string Diagnosis  = "diagnosis";
    public const string Lab        = "lab";
    public const string Radiology  = "radiology";
    public const string Attachment = "attachment";

    // serviceDetails.proidclaim must be serialized as string, not int (invariant §18.2).
    public const bool ServiceProIdClaimIsString = true;
}

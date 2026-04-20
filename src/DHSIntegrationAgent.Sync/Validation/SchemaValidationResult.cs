namespace DHSIntegrationAgent.Sync.Validation;

public sealed record SchemaValidationResult(
    bool IsValid,
    IReadOnlyList<SchemaValidationIssue> Issues);

public sealed record SchemaValidationIssue(
    string Gate,
    string Entity,
    string Column,      // empty string if issue is at entity level
    string Severity,    // "Error" | "Warning"
    string Message);

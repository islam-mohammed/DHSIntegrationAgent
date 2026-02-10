namespace DHSIntegrationAgent.Contracts.Claims;

public sealed record ClaimBundleValidationIssue(
    string IssueType,
    string? FieldPath,
    string? RawValue,
    string Message,
    bool IsBlocking
);

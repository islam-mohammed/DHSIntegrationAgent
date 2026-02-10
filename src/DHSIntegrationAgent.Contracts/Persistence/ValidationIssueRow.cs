namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record ValidationIssueRow(
    string ProviderDhsCode,
    int? ProIdClaim,
    string IssueType,
    string? FieldPath,
    string? RawValue,
    string Message,
    bool IsBlocking,
    DateTimeOffset CreatedUtc);

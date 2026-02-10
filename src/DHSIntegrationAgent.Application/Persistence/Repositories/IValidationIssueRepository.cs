namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public sealed record ValidationIssueRow(
    string ProviderDhsCode,
    int? ProIdClaim,
    string IssueType,
    string? FieldPath,
    string? RawValue,
    string Message,
    bool IsBlocking,
    DateTimeOffset CreatedUtc);

public interface IValidationIssueRepository
{
    Task InsertAsync(ValidationIssueRow row, CancellationToken cancellationToken);

    Task ResolveAsync(long validationIssueId, string resolvedBy, DateTimeOffset utcNow, CancellationToken cancellationToken);
}

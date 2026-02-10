using DHSIntegrationAgent.Contracts.Persistence;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IValidationIssueRepository
{
    Task InsertAsync(ValidationIssueRow row, CancellationToken cancellationToken);

    Task ResolveAsync(long validationIssueId, string resolvedBy, DateTimeOffset utcNow, CancellationToken cancellationToken);
}

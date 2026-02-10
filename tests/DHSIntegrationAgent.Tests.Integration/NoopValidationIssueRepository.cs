using DHSIntegrationAgent.Contracts.Persistence;
﻿using DHSIntegrationAgent.Application.Persistence.Repositories;

namespace DHSIntegrationAgent.Tests.Integration
{
    internal class NoopValidationIssueRepository : IValidationIssueRepository
    {
        public Task InsertAsync(ValidationIssueRow row, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task ResolveAsync(long validationIssueId, string resolvedBy, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
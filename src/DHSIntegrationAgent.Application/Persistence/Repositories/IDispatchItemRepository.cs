using DHSIntegrationAgent.Contracts.Persistence;
﻿using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IDispatchItemRepository
{
    Task InsertManyAsync(IReadOnlyList<DispatchItemRow> rows, CancellationToken cancellationToken);

    Task UpdateItemResultAsync(
        string dispatchId,
        IReadOnlyList<(ClaimKey Key, DispatchItemResult Result, string? ErrorMessage)> results,
        CancellationToken cancellationToken);
}

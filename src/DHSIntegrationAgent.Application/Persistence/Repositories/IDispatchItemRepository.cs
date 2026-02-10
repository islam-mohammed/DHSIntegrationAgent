using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public sealed record DispatchItemRow(
    string DispatchId,
    string ProviderDhsCode,
    int ProIdClaim,
    int ItemOrder,
    DispatchItemResult ItemResult,
    string? ErrorMessage);

public interface IDispatchItemRepository
{
    Task InsertManyAsync(IReadOnlyList<DispatchItemRow> rows, CancellationToken cancellationToken);

    Task UpdateItemResultAsync(
        string dispatchId,
        IReadOnlyList<(ClaimKey Key, DispatchItemResult Result, string? ErrorMessage)> results,
        CancellationToken cancellationToken);
}

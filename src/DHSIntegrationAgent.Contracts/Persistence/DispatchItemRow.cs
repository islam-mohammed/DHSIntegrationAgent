using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record DispatchItemRow(
    string DispatchId,
    string ProviderDhsCode,
    int ProIdClaim,
    int ItemOrder,
    DispatchItemResult ItemResult,
    string? ErrorMessage);

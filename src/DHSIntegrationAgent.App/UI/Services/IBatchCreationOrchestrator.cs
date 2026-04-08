using DHSIntegrationAgent.Contracts.Persistence;

namespace DHSIntegrationAgent.App.UI.Services;

public interface IBatchCreationOrchestrator
{
    Task<bool> ConfirmAndCreateBatchAsync(
        string companyCode,
        string payerName,
        int month,
        int year,
        bool isRecreation,
        IEnumerable<BatchRow> existingBatchesToDelete);
}

using System.Windows;
using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.App.UI.Services;

public class BatchCreationOrchestrator : IBatchCreationOrchestrator
{
    private readonly ISqliteUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IProviderTablesAdapter _tablesAdapter;
    private readonly ISystemClock _clock;
    private readonly IBatchTracker _batchTracker;
    private readonly IDeleteBatchService _deleteBatchService;
    private readonly IBatchClient _batchClient;
    private readonly IUserContext _userContext;

    public BatchCreationOrchestrator(
        ISqliteUnitOfWorkFactory unitOfWorkFactory,
        IProviderTablesAdapter tablesAdapter,
        ISystemClock clock,
        IBatchTracker batchTracker,
        IDeleteBatchService deleteBatchService,
        IBatchClient batchClient,
        IUserContext userContext)
    {
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _tablesAdapter = tablesAdapter ?? throw new ArgumentNullException(nameof(tablesAdapter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _batchTracker = batchTracker ?? throw new ArgumentNullException(nameof(batchTracker));
        _deleteBatchService = deleteBatchService ?? throw new ArgumentNullException(nameof(deleteBatchService));
        _batchClient = batchClient ?? throw new ArgumentNullException(nameof(batchClient));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    public async Task<bool> ConfirmAndCreateBatchAsync(
        string companyCode,
        string payerName,
        int month,
        int year,
        bool isRecreation,
        IEnumerable<BatchRow> existingBatchesToDelete)
    {
        try
        {
            // 1. Resolve ProviderDhsCode
            string? providerDhsCode;
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var settings = await uow.AppSettings.GetAsync(default);
                providerDhsCode = settings?.ProviderDhsCode;
            }

            if (string.IsNullOrWhiteSpace(providerDhsCode))
            {
                MessageBox.Show("ProviderDhsCode is not configured.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // 2. Determine Date Range
            var startDateOffset = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
            var endDateOffset = startDateOffset.AddMonths(1).AddTicks(-1);

            // 3. Check Integration Type and Validate Financials
            var integrationType = "Tables"; // Default

            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var profile = await uow.ProviderProfiles.GetActiveByProviderDhsCodeAsync(providerDhsCode, default);
                if (profile != null)
                {
                    integrationType = profile.IntegrationType;
                }
            }

            DHSIntegrationAgent.Contracts.Providers.FinancialSummary? summary = null;
            int initialTotalClaimsCount = 0;
            string actionText = isRecreation ? "recreating" : "creating";
            string actionSuffix = isRecreation ? " The old batch will be deleted." : "";

            if (integrationType.Equals("Tables", StringComparison.OrdinalIgnoreCase))
            {
                summary = await _tablesAdapter.GetFinancialSummaryAsync(
                    providerDhsCode,
                    companyCode,
                    startDateOffset,
                    endDateOffset,
                    default);

                initialTotalClaimsCount = summary.TotalClaims;

                var message = $"Batch Validation Summary for {payerName} ({month}/{year}):\n\n" +
                              $"Total Claims: {summary.TotalClaims}\n" +
                              $"Claimed Amount: {summary.TotalClaimedAmount:N2}\n" +
                              $"Total Discount: {summary.TotalDiscount:N2}\n" +
                              $"Total Deductible: {summary.TotalDeductible:N2}\n" +
                              $"Total Net Amount: {summary.TotalNetAmount:N2}\n\n" +
                              $"Financial Validation: {(summary.IsValid ? "VALID ✅" : "INVALID ❌")}\n\n" +
                              $"Do you want to proceed with {actionText} this batch and starting the stream?{actionSuffix}";

                var result = MessageBox.Show(message, "Confirm Batch Action", MessageBoxButton.YesNo, summary.IsValid ? MessageBoxImage.Question : MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    return false;
                }
            }
            else
            {
                var count = await _tablesAdapter.CountClaimsAsync(providerDhsCode, companyCode, startDateOffset, endDateOffset, default);
                initialTotalClaimsCount = count;
                string capitalizeActionText = isRecreation ? "Recreate" : "Create";
                var result = MessageBox.Show($"{capitalizeActionText} batch for {payerName} with {count} claims?{actionSuffix}", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    return false;
                }
            }

            // 4. Clear existing batches associated data and mark as deleted
            if (existingBatchesToDelete != null && existingBatchesToDelete.Any())
            {
                foreach (var batchToDelete in existingBatchesToDelete)
                {
                    if (isRecreation)
                    {
                        var bcrIdStr = !string.IsNullOrEmpty(batchToDelete.BcrId) ? batchToDelete.BcrId : null;
                        var delResult = await _deleteBatchService.DeleteBatchAsync(batchToDelete.BatchId, bcrIdStr, default);
                        if (!delResult.Succeeded)
                        {
                            MessageBox.Show(delResult.ErrorMessage ?? "Failed to delete old batch.", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return false;
                        }
                    }
                    else
                    {
                        // Delete from API
                        if (!string.IsNullOrEmpty(batchToDelete.BcrId) && int.TryParse(batchToDelete.BcrId, out int bcrIdInt))
                        {
                            var delResult = await _batchClient.DeleteBatchAsync(bcrIdInt, default);
                            if (!delResult.Succeeded)
                            {
                                MessageBox.Show($"Failed to delete batch from server: {delResult.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                return false;
                            }
                        }

                        // Soft delete locally and clear associated data
                        await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
                        {
                            await uow.Batches.UpdateStatusAsync(batchToDelete.BatchId, BatchStatus.Deleted, null, null, DateTimeOffset.UtcNow, default);
                            await uow.Batches.ClearBatchDataAsync(batchToDelete.BatchId, default);
                            await uow.CommitAsync(default);
                        }
                    }
                }
            }

            // 5. Create Batch locally
            long batchId;
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var monthKey = $"{year}{month:D2}";
                var key = new BatchKey(providerDhsCode, companyCode, monthKey, startDateOffset, endDateOffset);

                // Start as Draft to allow Fetch & Stage to pick it up properly (isolated)
                batchId = await uow.Batches.EnsureBatchAsync(key, BatchStatus.Draft, initialTotalClaimsCount, _clock.UtcNow, default, _userContext.UserName);
                await uow.CommitAsync(default);
            }

            // 6. Run Stream A Fetch & Stage immediately (via Tracker)
            BatchRow? batchRow;
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                batchRow = await uow.Batches.GetByIdAsync(batchId, default);
            }

            if (batchRow != null)
            {
                _batchTracker.TrackBatchCreation(batchRow, summary);
            }

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error processing batch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}

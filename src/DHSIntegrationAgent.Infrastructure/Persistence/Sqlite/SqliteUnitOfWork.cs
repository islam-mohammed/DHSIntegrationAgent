using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Application.Security;
using DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

internal sealed class SqliteUnitOfWork : ISqliteUnitOfWork
{
    private readonly DbConnection _conn;
    private readonly DbTransaction _tx;
    private bool _completed;

    /// <summary>
    /// Why this ctor exists:
    /// - A UnitOfWork owns a single open connection + single transaction.
    /// - Repositories share that same transaction to guarantee atomic operations.
    /// - encryptor is injected so PHI repos can encrypt/decrypt at the boundary (WBS 1.3).
    /// </summary>
    public SqliteUnitOfWork(DbConnection conn, DbTransaction tx, IColumnEncryptor encryptor)
    {
        _conn = conn;
        _tx = tx;

        // Core settings + config repos
        AppSettings = new AppSettingsRepository(_conn, _tx);
        ProviderProfiles = new ProviderProfileRepository(_conn, _tx);
        ProviderExtractionConfigs = new ProviderExtractionConfigRepository(_conn, _tx);
        ProviderConfigCache = new ProviderConfigCacheRepository(_conn, _tx);
        Payers = new PayerProfileRepository(_conn, _tx);

        // Batch/Claim state machine repos
        Batches = new BatchRepository(_conn, _tx);
        Claims = new ClaimRepository(_conn, _tx);

        // PHI repos (WBS 1.3: encrypt/decrypt handled here)
        ClaimPayloads = new ClaimPayloadRepository(_conn, _tx, encryptor);
        Attachments = new AttachmentRepository(_conn, _tx, encryptor);

        // Mapping + dispatch repos
        DomainMappings = new DomainMappingRepository(_conn, _tx);
        Dispatches = new DispatchRepository(_conn, _tx);
        DispatchItems = new DispatchItemRepository(_conn, _tx);

        // Validation + observability persistence
        ValidationIssues = new ValidationIssueRepository(_conn, _tx);
        ApiCallLogs = new ApiCallLogRepository(_conn, _tx);
    }

    // -------- ISqliteUnitOfWork properties (must match the interface exactly) --------

    public IAppSettingsRepository AppSettings { get; }
    public IProviderProfileRepository ProviderProfiles { get; }
    public IProviderExtractionConfigRepository ProviderExtractionConfigs { get; }
    public IProviderConfigCacheRepository ProviderConfigCache { get; }
    public IPayerProfileRepository Payers { get; }

    public IBatchRepository Batches { get; }
    public IClaimRepository Claims { get; }
    public IClaimPayloadRepository ClaimPayloads { get; }

    public IDomainMappingRepository DomainMappings { get; }

    public IDispatchRepository Dispatches { get; }
    public IDispatchItemRepository DispatchItems { get; }

    public IAttachmentRepository Attachments { get; }
    public IValidationIssueRepository ValidationIssues { get; }

    public IApiCallLogRepository ApiCallLogs { get; }

    // -------- Transaction control --------

    /// <summary>
    /// Why Commit exists:
    /// - We commit only after all repository operations succeed.
    /// - Prevents partial writes across multiple tables (atomicity).
    /// </summary>
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_completed) return;
        await _tx.CommitAsync(cancellationToken);
        _completed = true;
    }

    /// <summary>
    /// Why Rollback exists:
    /// - If something fails mid-operation, rollback returns DB to prior consistent state.
    /// </summary>
    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (_completed) return;
        await _tx.RollbackAsync(cancellationToken);
        _completed = true;
    }

    /// <summary>
    /// Why Dispose rolls back if not completed:
    /// - Protects you from forgetting Commit().
    /// - Keeps DB consistent in exception paths.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_completed)
                await _tx.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Swallow rollback failures on dispose; WBS 1.4 recovery handles stale leases.
        }
        finally
        {
            await _tx.DisposeAsync();
            await _conn.DisposeAsync();
        }
    }
}

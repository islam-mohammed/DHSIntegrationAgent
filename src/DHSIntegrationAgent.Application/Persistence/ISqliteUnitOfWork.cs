using DHSIntegrationAgent.Application.Persistence.Repositories;

namespace DHSIntegrationAgent.Application.Persistence;

public interface ISqliteUnitOfWork : IAsyncDisposable
{
    IAppSettingsRepository AppSettings { get; }
    IProviderProfileRepository ProviderProfiles { get; }
    IProviderExtractionConfigRepository ProviderExtractionConfigs { get; }
    IProviderConfigCacheRepository ProviderConfigCache { get; }
    IPayerProfileRepository Payers { get; }

    IBatchRepository Batches { get; }
    IClaimRepository Claims { get; }
    IClaimPayloadRepository ClaimPayloads { get; }

    IDomainMappingRepository DomainMappings { get; }

    IDispatchRepository Dispatches { get; }
    IDispatchItemRepository DispatchItems { get; }

    IAttachmentRepository Attachments { get; }

    IApiCallLogRepository ApiCallLogs { get; }

    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Contracts.DomainMapping;
using DHSIntegrationAgent.Domain.WorkStates;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Providers;

public sealed class DomainMappingOrchestrator : IDomainMappingOrchestrator
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IProviderConfigurationService _configService;
    private readonly IDomainMappingClient _client;
    private readonly ILogger<DomainMappingOrchestrator> _logger;

    public DomainMappingOrchestrator(
        ISqliteUnitOfWorkFactory uowFactory,
        IProviderConfigurationService configService,
        IDomainMappingClient client,
        ILogger<DomainMappingOrchestrator> logger)
    {
        _uowFactory = uowFactory;
        _configService = configService;
        _client = client;
        _logger = logger;
    }

    public async Task RefreshFromProviderConfigAsync(CancellationToken ct)
    {
        string? providerDhsCode = null;
        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            var settings = await uow.AppSettings.GetAsync(ct);
            providerDhsCode = settings.ProviderDhsCode;
        }

        if (string.IsNullOrWhiteSpace(providerDhsCode))
        {
            _logger.LogWarning("Cannot refresh domain mappings: ProviderDhsCode is missing from AppSettings.");
            return;
        }

        await _configService.RefreshDomainMappingsAsync(providerDhsCode, ct);
    }

    public async Task PostMissingNowAsync(CancellationToken ct)
    {
        string? providerDhsCode = null;
        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            var settings = await uow.AppSettings.GetAsync(ct);
            providerDhsCode = settings.ProviderDhsCode;
        }

        if (string.IsNullOrWhiteSpace(providerDhsCode))
            return;

        IReadOnlyList<DHSIntegrationAgent.Contracts.Persistence.MissingDomainMappingRow> missing;
        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            missing = await uow.DomainMappings.ListEligibleForPostingAsync(providerDhsCode, ct);
        }

        if (missing.Count == 0)
            return;

        var itemsToPost = missing.Select(m => new MismappedItem(
            m.SourceValue,
            m.ProviderNameValue ?? m.SourceValue,
            m.DomainTableId,
            m.DomainTableName
        )).ToList();

        var request = new InsertMissMappingDomainRequest(
            providerDhsCode,
            itemsToPost
        );

        var result = await _client.InsertMissMappingDomainAsync(request, ct);

        var now = DateTimeOffset.UtcNow;
        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            if (result.Succeeded)
            {
                foreach (var m in missing)
                {
                    await uow.DomainMappings.UpdateMissingStatusAsync(
                        m.MissingMappingId,
                        MappingStatus.Posted,
                        now,
                        now,
                        ct);
                }
            }
            else
            {
                foreach (var m in missing)
                {
                    await uow.DomainMappings.UpdateMissingStatusAsync(
                        m.MissingMappingId,
                        MappingStatus.PostFailed,
                        now,
                        now,
                        ct);
                }
            }

            await uow.CommitAsync(ct);
        }
    }
}

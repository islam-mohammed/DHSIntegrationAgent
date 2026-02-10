using System.Collections.ObjectModel;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Providers;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class MappingDomainViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IProviderConfigurationService _configService;
    private bool _isLoading;

    public ObservableCollection<MappingDomainRow> MappingDomains { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand LoadCommand { get; }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public MappingDomainViewModel(ISqliteUnitOfWorkFactory uowFactory, IProviderConfigurationService configService)
    {
        _uowFactory = uowFactory;
        _configService = configService;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;
            await using var uow = await _uowFactory.CreateAsync(CancellationToken.None);
            var settings = await uow.AppSettings.GetAsync(CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(settings.ProviderDhsCode))
            {
                await _configService.RefreshDomainMappingsAsync(settings.ProviderDhsCode, CancellationToken.None);
            }
        }
        catch { /* ignore refresh errors for now, load what we have */ }
        finally
        {
            IsLoading = false;
        }

        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;

            await using var uow = await _uowFactory.CreateAsync(CancellationToken.None);
            var approved = await uow.DomainMappings.GetAllApprovedAsync(CancellationToken.None);
            var missing = await uow.DomainMappings.GetAllMissingAsync(CancellationToken.None);

            MappingDomains.Clear();

            foreach (var mapping in approved)
            {
                MappingDomains.Add(new MappingDomainRow
                {
                    ProviderDomainCode = mapping.ProviderDomainCode ?? mapping.ProviderDhsCode,
                    ProviderDomainValue = mapping.SourceValue,
                    DhsDomainValue = mapping.TargetValue,
                    IsDefault = mapping.IsDefault.HasValue ? (mapping.IsDefault.Value ? 1 : 0) : 0,
                    CodeValue = mapping.CodeValue ?? mapping.DomainName,
                    DisplayValue = mapping.DisplayValue ?? $"{mapping.DomainName} - Approved"
                });
            }

            foreach (var mapping in missing)
            {
                MappingDomains.Add(new MappingDomainRow
                {
                    ProviderDomainCode = mapping.ProviderDhsCode,
                    ProviderDomainValue = mapping.SourceValue,
                    DhsDomainValue = "",
                    IsDefault = 0,
                    CodeValue = mapping.DomainTableName ?? mapping.DomainName,
                    DisplayValue = mapping.ProviderNameValue ?? $"{mapping.DomainName} - Missing ({mapping.DiscoverySource})"
                });
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public sealed class MappingDomainRow
    {
        public string ProviderDomainCode { get; set; } = "";
        public string ProviderDomainValue { get; set; } = "";
        public string DhsDomainValue { get; set; } = "";
        public int IsDefault { get; set; }
        public string CodeValue { get; set; } = "";
        public string DisplayValue { get; set; } = "";
    }
}

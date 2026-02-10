using System.Collections.ObjectModel;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Persistence;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class MappingDomainViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private bool _isLoading;

    public ObservableCollection<MappingDomainRow> MappingDomains { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand LoadCommand { get; }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public MappingDomainViewModel(ISqliteUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;

            await using var uow = await _uowFactory.CreateAsync(CancellationToken.None);
            var mappings = await uow.DomainMappings.GetAllAsync(CancellationToken.None);

            MappingDomains.Clear();

            foreach (var mapping in mappings)
            {
                MappingDomains.Add(new MappingDomainRow
                {
                    ProviderDomainCode = mapping.ProviderDhsCode,
                    ProviderDomainValue = mapping.SourceValue,
                    DhsDomainValue = mapping.TargetValue ?? "",
                    IsDefault = 0, // No default field in DomainMapping table
                    CodeValue = mapping.DomainName,
                    DisplayValue = $"{mapping.DomainName} - {mapping.MappingStatus}"
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

using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class DomainMappingsViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IDomainMappingClient _domainMappingClient;
    private bool _isLoading;

    public ObservableCollection<MappingRow> MissingMappings { get; } = new();
    public ObservableCollection<MappingRow> PostedMappings { get; } = new();
    public ObservableCollection<MappingRow> ApprovedMappings { get; } = new();
    public ObservableCollection<DomainMappingRow> DomainMappings { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand PostNowCommand { get; }
    public AsyncRelayCommand LoadMissingMappingsCommand { get; }

    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public MappingDomainViewModel MappingDomainViewModel { get; }

    public DomainMappingsViewModel(
        ISqliteUnitOfWorkFactory uowFactory,
        IDomainMappingClient domainMappingClient)
    {
        _uowFactory = uowFactory;
        _domainMappingClient = domainMappingClient;

        RefreshCommand = new AsyncRelayCommand(LoadMissingMappingsAsync);
        LoadMissingMappingsCommand = new AsyncRelayCommand(LoadMissingMappingsAsync);
        PostNowCommand = new RelayCommand(() => { /* screen-only */ });

        MappingDomainViewModel = new MappingDomainViewModel(uowFactory);
    }

    public async Task LoadMissingMappingsAsync()
    {
        try
        {
            IsLoading = true;
            DomainMappings.Clear();

            // Temporary delay to see loading indicator (REMOVE AFTER TESTING)
            await Task.Delay(2000);

            // Get ProviderDhsCode from ProviderProfile
            await using var uow = await _uowFactory.CreateAsync(CancellationToken.None);

            // Get the first active provider profile (adjust logic as needed)
            var appSettings = await uow.AppSettings.GetAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(appSettings.ProviderDhsCode))
            {
                MessageBox.Show(
                    "ProviderDhsCode is not configured in AppSettings.\n\nPlease configure it in the Settings page first.",
                    "Configuration Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var providerDhsCode = appSettings.ProviderDhsCode;

            // Call API
            var result = await _domainMappingClient.GetMissingDomainMappingsAsync(providerDhsCode, CancellationToken.None);

            if (!result.Succeeded)
            {
                var errorMessage = result.Message ?? "Failed to load domain mappings.";

                // Add more debugging info for 404 errors
                if (result.StatusCode == 404)
                {
                    errorMessage = $"API Endpoint Not Found (HTTP 404)\n\n" +
                                   $"Endpoint: api/DomainMapping/GetMissingDomainMappings\n" +
                                   $"ProviderDhsCode: {providerDhsCode}\n\n" +
                                   $"Possible causes:\n" +
                                   $"1. The API endpoint is not implemented on the backend\n" +
                                   $"2. The ProviderDhsCode '{providerDhsCode}' doesn't exist in the backend database\n" +
                                   $"3. The endpoint URL format is incorrect\n\n" +
                                   $"Please verify the API is running and the endpoint exists.";
                }
                else if (result.Errors?.Any() == true)
                {
                    errorMessage += "\n\nErrors:\n" + string.Join("\n", result.Errors);
                }

                MessageBox.Show(errorMessage, "API Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (result.Data != null)
            {
                foreach (var item in result.Data)
                {
                    DomainMappings.Add(new DomainMappingRow
                    {
                        DomTableId = item.DomainTableId,
                        ProviderCode = item.ProviderCodeValue ?? "",
                        ProviderName = item.ProviderNameValue ?? "",
                        DomainTableName = item.DomainTableName ?? "",
                        DhsDomainValue = 0, // Not provided by API for missing mappings
                        IsDefault = 0,
                        DisplayValue = "" // Not provided by API for missing mappings
                    });
                }

                if (DomainMappings.Count == 0)
                {
                    MessageBox.Show(
                        $"No missing domain mappings found for ProviderDhsCode: {providerDhsCode}\n\n" +
                        $"All domains are already mapped!",
                        "No Missing Mappings",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }
        catch (HttpRequestException httpEx)
        {
            MessageBox.Show(
                $"Network Error: Unable to connect to the API.\n\n" +
                $"Error: {httpEx.Message}\n\n" +
                $"Please check:\n" +
                $"1. The API service is running\n" +
                $"2. The API URL is configured correctly\n" +
                $"3. Network connectivity",
                "Network Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error loading domain mappings:\n\n{ex.Message}\n\n" +
                $"Stack Trace:\n{ex.StackTrace}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public sealed class MappingRow
    {
        public string Domain { get; set; } = "";
        public string ProviderCode { get; set; } = "";
        public string ProviderValue { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public sealed class DomainMappingRow
    {
        public int DomTableId { get; set; }
        public string ProviderCode { get; set; } = "";
        public string ProviderName { get; set; } = "";
        public string DomainTableName { get; set; } = "";
        public int DhsDomainValue { get; set; }
        public int IsDefault { get; set; }
        public string DisplayValue { get; set; } = "";
    }
}

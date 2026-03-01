using DHSIntegrationAgent.Contracts.DomainMapping;
﻿using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Providers;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class DomainMappingsViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IDomainMappingOrchestrator _orchestrator;
    private bool _isLoading;

    public ObservableCollection<MappingRow> MissingMappings { get; } = new();
    public ObservableCollection<MappingRow> PostedMappings { get; } = new();
    public ObservableCollection<MappingRow> ApprovedMappings { get; } = new();
    public ObservableCollection<DomainMappingRow> DomainMappings { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand PostNowCommand { get; }
    public AsyncRelayCommand LoadMissingMappingsCommand { get; }

    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public MappingDomainViewModel MappingDomainViewModel { get; }

    public DomainMappingsViewModel(
        ISqliteUnitOfWorkFactory uowFactory,
        IDomainMappingOrchestrator orchestrator)
    {
        _uowFactory = uowFactory;
        _orchestrator = orchestrator;

        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync);
        LoadMissingMappingsCommand = new AsyncRelayCommand(LoadMissingMappingsAsync);
        PostNowCommand = new AsyncRelayCommand(PostMissingNowAsync);

        MappingDomainViewModel = new MappingDomainViewModel(uowFactory, orchestrator);
    }

    public async Task RefreshAllAsync()
    {
        await _orchestrator.RefreshFromProviderConfigAsync(CancellationToken.None);
        await LoadMissingMappingsAsync();
        await MappingDomainViewModel.LoadAsync();
    }

    private async Task PostMissingNowAsync()
    {
        try
        {
            IsLoading = true;
            await _orchestrator.PostMissingNowAsync(CancellationToken.None);
            await LoadMissingMappingsAsync();
            await MappingDomainViewModel.LoadAsync();
            MessageBox.Show("Finished posting missing mappings.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error posting mappings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadMissingMappingsAsync()
    {
        try
        {
            IsLoading = true;
            DomainMappings.Clear();

            // Get ProviderDhsCode from ProviderProfile
            string providerDhsCode;
            await using (var uow = await _uowFactory.CreateAsync(CancellationToken.None))
            {
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
                providerDhsCode = appSettings.ProviderDhsCode;
            }

            // Read from SQLite
            IReadOnlyList<DHSIntegrationAgent.Contracts.Persistence.MissingDomainMappingRow> missing;
            await using (var uow = await _uowFactory.CreateAsync(CancellationToken.None))
            {
                missing = await uow.DomainMappings.GetAllMissingAsync(CancellationToken.None);
            }

            foreach (var item in missing.Where(m => m.ProviderDhsCode == providerDhsCode))
            {
                DomainMappings.Add(new DomainMappingRow
                {
                    DomTableId = item.DomainTableId,
                    ProviderCode = item.SourceValue ?? "",
                    ProviderName = item.ProviderNameValue ?? item.SourceValue ?? "",
                    DomainTableName = item.DomainTableName ?? item.DomainName ?? "",
                    DhsDomainValue = 0,
                    IsDefault = 0,
                    DisplayValue = ""
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

using System.Collections.ObjectModel;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Persistence;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class DiagnosticsViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _unitOfWorkFactory;

    public ObservableCollection<ApiCallRow> ApiCalls { get; } = new();

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand ExportSupportBundleCommand { get; }

    public LogsViewModel LogsViewModel { get; }

    public DiagnosticsViewModel(ISqliteUnitOfWorkFactory unitOfWorkFactory)
    {
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));

        RefreshCommand = new AsyncRelayCommand(LoadApiCallsAsync);
        ExportSupportBundleCommand = new RelayCommand(() =>
        {
            // Screen-only: real export is WBS 6.2 + 5.8 wiring
            // Must remain PHI-safe (no payloads/attachments).
        });

        LogsViewModel = new LogsViewModel();

        // Load data on initialization
        _ = LoadApiCallsAsync();
    }

    private async Task LoadApiCallsAsync()
    {
        IsLoading = true;
        try
        {
            // Add 2000ms delay to show loading animation
            await Task.Delay(2000);

            ApiCalls.Clear();

            await using var uow = await _unitOfWorkFactory.CreateAsync(default);
            var apiCallLogs = await uow.ApiCallLogs.GetRecentApiCallsAsync(limit: 100, default);

            foreach (var log in apiCallLogs)
            {
                ApiCalls.Add(new ApiCallRow
                {
                    ProviderDhsCode = log.ProviderDhsCode ?? "—",
                    EndpointName = log.EndpointName,
                    CorrelationId = log.CorrelationId ?? "—",
                    RequestUtc = log.RequestUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    ResponseUtc = log.ResponseUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—",
                    DurationMs = log.DurationMs?.ToString() ?? "—",
                    HttpStatusCode = log.HttpStatusCode?.ToString() ?? "—",
                    Succeeded = log.Succeeded ? "Yes" : "No",
                    ErrorMessage = log.ErrorMessage ?? "—",
                    RequestBytes = log.RequestBytes?.ToString() ?? "—",
                    ResponseBytes = log.ResponseBytes?.ToString() ?? "—",
                    WasGzipRequest = log.WasGzipRequest ? "Yes" : "No"
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading API calls: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string ExtractMethod(string endpointName)
    {
        // Try to extract HTTP method from endpoint name
        // Examples: "POST /api/batch", "GET /api/config"
        if (endpointName.StartsWith("GET ", StringComparison.OrdinalIgnoreCase))
            return "GET";
        if (endpointName.StartsWith("POST ", StringComparison.OrdinalIgnoreCase))
            return "POST";
        if (endpointName.StartsWith("PUT ", StringComparison.OrdinalIgnoreCase))
            return "PUT";
        if (endpointName.StartsWith("DELETE ", StringComparison.OrdinalIgnoreCase))
            return "DELETE";

        // Default to GET if no method prefix
        return "GET";
    }

    public sealed class ApiCallRow
    {
        public string ProviderDhsCode { get; set; } = "";
        public string EndpointName { get; set; } = "";
        public string CorrelationId { get; set; } = "";
        public string RequestUtc { get; set; } = "";
        public string ResponseUtc { get; set; } = "";
        public string DurationMs { get; set; } = "";
        public string HttpStatusCode { get; set; } = "";
        public string Succeeded { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public string RequestBytes { get; set; } = "";
        public string ResponseBytes { get; set; } = "";
        public string WasGzipRequest { get; set; } = "";
    }
}

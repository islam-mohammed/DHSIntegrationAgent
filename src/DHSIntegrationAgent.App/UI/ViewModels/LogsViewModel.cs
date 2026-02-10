using System.Collections.ObjectModel;
using DHSIntegrationAgent.App.UI.Mvvm;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class LogsViewModel : ViewModelBase
{
    public ObservableCollection<ApiCallLogRow> ApiCallLogs { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public LogsViewModel()
    {
        RefreshCommand = new RelayCommand(() => { /* screen-only */ });

        // Sample data
        ApiCallLogs.Add(new ApiCallLogRow
        {
            ApiCallLogId = 1,
            ProviderDhsCode = "BCH-001",
            EndpointName = "/api/claims",
            CorrelationId = "abc-123-def",
            RequestUtc = "2024-01-15 08:30:00",
            ResponseUtc = "2024-01-15 08:30:02",
            DurationMs = 2000,
            HttpStatusCode = 200,
            Succeeded = true,
            ErrorMessage = "",
            RequestBytes = 1024,
            ResponseBytes = 2048,
            WasGzipRequest = 1
        });
    }

    public sealed class ApiCallLogRow
    {
        public int ApiCallLogId { get; set; }
        public string ProviderDhsCode { get; set; } = "";
        public string EndpointName { get; set; } = "";
        public string CorrelationId { get; set; } = "";
        public string RequestUtc { get; set; } = "";
        public string ResponseUtc { get; set; } = "";
        public int DurationMs { get; set; }
        public int HttpStatusCode { get; set; }
        public bool Succeeded { get; set; }
        public string ErrorMessage { get; set; } = "";
        public int RequestBytes { get; set; }
        public int ResponseBytes { get; set; }
        public int WasGzipRequest { get; set; }
    }
}

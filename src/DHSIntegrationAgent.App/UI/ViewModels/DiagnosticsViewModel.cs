using System.Collections.ObjectModel;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class DiagnosticsViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IHealthClient _healthClient;

    // ── API Call Logs tab ────────────────────────────────────────────────────
    public ObservableCollection<ApiCallRow> ApiCalls { get; } = new();
    public PaginationViewModel ApiCallsPagination { get; } = new();

    // ── Failed Claims tab ────────────────────────────────────────────────────
    public ObservableCollection<FailedClaimRow> FailedClaims { get; } = new();
    public PaginationViewModel FailedClaimsPagination { get; } = new();

    // ── Shared state ─────────────────────────────────────────────────────────
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _isLoadingClaims;
    public bool IsLoadingClaims
    {
        get => _isLoadingClaims;
        set => SetProperty(ref _isLoadingClaims, value);
    }

    private bool _isCheckingHealth;
    public bool IsCheckingHealth
    {
        get => _isCheckingHealth;
        set => SetProperty(ref _isCheckingHealth, value);
    }

    private string? _apiHealthMessage;
    public string? ApiHealthMessage
    {
        get => _apiHealthMessage;
        set => SetProperty(ref _apiHealthMessage, value);
    }

    // ── Commands ─────────────────────────────────────────────────────────────
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand RefreshFailedClaimsCommand { get; }
    public RelayCommand ExportSupportBundleCommand { get; }
    public AsyncRelayCommand CheckHealthCommand { get; }

    public DiagnosticsViewModel(
        ISqliteUnitOfWorkFactory unitOfWorkFactory,
        IHealthClient healthClient)
    {
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _healthClient = healthClient ?? throw new ArgumentNullException(nameof(healthClient));

        RefreshCommand = new AsyncRelayCommand(LoadApiCallsAsync);
        RefreshFailedClaimsCommand = new AsyncRelayCommand(LoadFailedClaimsAsync);
        CheckHealthCommand = new AsyncRelayCommand(CheckHealthAsync);
        ExportSupportBundleCommand = new RelayCommand(() => { });

        ApiCallsPagination.PageChanged += (_, _) => _ = LoadApiCallsAsync();
        FailedClaimsPagination.PageChanged += (_, _) => _ = LoadFailedClaimsAsync();

        _ = LoadApiCallsAsync();
        _ = LoadFailedClaimsAsync();
    }

    private async Task CheckHealthAsync()
    {
        if (IsCheckingHealth) return;

        IsCheckingHealth = true;
        ApiHealthMessage = "Checking API Health...";

        try
        {
            var result = await _healthClient.CheckApiHealthAsync(CancellationToken.None);
            ApiHealthMessage = result.Succeeded
                ? $"✅ Connection successful ({DHSIntegrationAgent.Application.Helpers.DateTimeHelper.GetKSADateTime():HH:mm:ss})"
                : $"❌ Health check failed: {result.Message}";

            await LoadApiCallsAsync();
        }
        catch (Exception ex)
        {
            ApiHealthMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsCheckingHealth = false;
        }
    }

    private async Task LoadApiCallsAsync()
    {
        IsLoading = true;
        try
        {
            await using var uow = await _unitOfWorkFactory.CreateAsync(default);

            var totalCount = await uow.ApiCallLogs.CountApiCallsAsync(default);
            ApiCallsPagination.SetTotalCount(totalCount);

            var logs = await uow.ApiCallLogs.GetApiCallsPagedAsync(
                ApiCallsPagination.CurrentPage - 1,
                ApiCallsPagination.PageSize,
                default);

            ApiCalls.Clear();
            foreach (var log in logs)
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

    private async Task LoadFailedClaimsAsync()
    {
        IsLoadingClaims = true;
        try
        {
            await using var uow = await _unitOfWorkFactory.CreateAsync(default);

            var settings = await uow.AppSettings.GetAsync(default);
            var providerDhsCode = settings.ProviderDhsCode ?? "";

            var totalCount = await uow.Claims.CountFailedClaimsAsync(providerDhsCode, default);
            FailedClaimsPagination.SetTotalCount(totalCount);

            var items = await uow.Claims.GetFailedClaimsPagedAsync(
                providerDhsCode,
                FailedClaimsPagination.CurrentPage - 1,
                FailedClaimsPagination.PageSize,
                default);

            FailedClaims.Clear();
            foreach (var item in items)
            {
                FailedClaims.Add(new FailedClaimRow
                {
                    ProIdClaim = item.ProIdClaim.ToString(),
                    CompanyCode = item.CompanyCode,
                    MonthKey = item.MonthKey,
                    BatchId = item.BatchId?.ToString() ?? "—",
                    AttemptCount = item.AttemptCount.ToString(),
                    LastError = item.LastError ?? "—",
                    LastUpdatedUtc = item.LastUpdatedUtc.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading failed claims: {ex.Message}");
        }
        finally
        {
            IsLoadingClaims = false;
        }
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

    public sealed class FailedClaimRow
    {
        public string ProIdClaim { get; set; } = "";
        public string CompanyCode { get; set; } = "";
        public string MonthKey { get; set; } = "";
        public string BatchId { get; set; } = "";
        public string AttemptCount { get; set; } = "";
        public string LastError { get; set; } = "";
        public string LastUpdatedUtc { get; set; } = "";
    }
}

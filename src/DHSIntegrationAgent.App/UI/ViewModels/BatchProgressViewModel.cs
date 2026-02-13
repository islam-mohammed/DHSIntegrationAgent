using DHSIntegrationAgent.App.UI.Mvvm;

namespace DHSIntegrationAgent.App.UI.ViewModels;

/// <summary>
/// ViewModel representing the progress of a single batch creation/fetching task.
/// </summary>
public sealed class BatchProgressViewModel : ViewModelBase
{
    private long _internalBatchId;
    private string _batchNumber = "Pending...";
    private string _statusMessage = "Initializing...";
    private string _financialMessage = "";
    private int _totalClaims;
    private int _processedClaims;
    private bool _isCompleted;
    private bool _isError;

    public long InternalBatchId
    {
        get => _internalBatchId;
        set => SetProperty(ref _internalBatchId, value);
    }

    public string BatchNumber
    {
        get => _batchNumber;
        set => SetProperty(ref _batchNumber, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string FinancialMessage
    {
        get => _financialMessage;
        set => SetProperty(ref _financialMessage, value);
    }

    public int TotalClaims
    {
        get => _totalClaims;
        set
        {
            if (SetProperty(ref _totalClaims, value))
            {
                OnPropertyChanged(nameof(RemainingClaims));
                OnPropertyChanged(nameof(ProgressPercentage));
            }
        }
    }

    public int ProcessedClaims
    {
        get => _processedClaims;
        set
        {
            if (SetProperty(ref _processedClaims, value))
            {
                OnPropertyChanged(nameof(RemainingClaims));
                OnPropertyChanged(nameof(ProgressPercentage));
            }
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }

    public bool IsError
    {
        get => _isError;
        set => SetProperty(ref _isError, value);
    }

    public int RemainingClaims => Math.Max(0, TotalClaims - ProcessedClaims);

    public double ProgressPercentage => TotalClaims > 0
        ? Math.Min(100.0, (double)ProcessedClaims / TotalClaims * 100.0)
        : 0;
}

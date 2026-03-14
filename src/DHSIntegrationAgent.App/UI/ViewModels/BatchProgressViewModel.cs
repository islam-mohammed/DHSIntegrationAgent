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
    private int _totalClaims;
    private int _processedClaims;
    private int _failedClaims;
    private string _totalLabel = "Total Claims";
    private bool _hasFailedClaims;
    private double? _percentageOverride;
    private bool _isCompleted;
    private bool _isError;
    private bool _isSending;

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

    public int FailedClaims
    {
        get => _failedClaims;
        set
        {
            if (SetProperty(ref _failedClaims, value))
            {
                OnPropertyChanged(nameof(RemainingClaims));
                OnPropertyChanged(nameof(ProgressPercentage));
            }
        }
    }

    public string TotalLabel
    {
        get => _totalLabel;
        set
        {
            if (SetProperty(ref _totalLabel, value))
            {
                OnPropertyChanged(nameof(ProcessedLabel));
            }
        }
    }

    public bool HasFailedClaims
    {
        get => _hasFailedClaims;
        set => SetProperty(ref _hasFailedClaims, value);
    }

    public double? PercentageOverride
    {
        get => _percentageOverride;
        set
        {
            if (SetProperty(ref _percentageOverride, value))
            {
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

    public bool IsSending
    {
        get => _isSending;
        set
        {
            if (SetProperty(ref _isSending, value))
            {
                OnPropertyChanged(nameof(ProcessedLabel));
            }
        }
    }

    public string ProcessedLabel
    {
        get
        {
            if (TotalLabel == "Total Attachments") return "Uploaded";
            return IsSending ? "Sent" : "Fetched";
        }
    }

    public int RemainingClaims => Math.Max(0, TotalClaims - ProcessedClaims - FailedClaims);

    public double ProgressPercentage
    {
        get
        {
            if (PercentageOverride.HasValue) return PercentageOverride.Value;

            return TotalClaims > 0
                ? Math.Min(100.0, (double)(ProcessedClaims + FailedClaims) / TotalClaims * 100.0)
                : 0;
        }
    }
}

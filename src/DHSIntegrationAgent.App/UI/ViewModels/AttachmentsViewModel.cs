using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.App.UI.Navigation;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class AttachmentsViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly INavigationService _navigation;
    private long _batchId;

    public ObservableCollection<AttachmentRow> UploadQueue { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand RetrySelectedCommand { get; }
    public RelayCommand GoBackCommand { get; }

    private AttachmentRow? _selected;
    public AttachmentRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    private string _batchInfo = "";
    public string BatchInfo
    {
        get => _batchInfo;
        set => SetProperty(ref _batchInfo, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public AttachmentsViewModel(ISqliteUnitOfWorkFactory uowFactory, INavigationService navigation)
    {
        _uowFactory = uowFactory;
        _navigation = navigation;

        RefreshCommand = new RelayCommand(async () => await LoadAttachmentsAsync());
        RetrySelectedCommand = new RelayCommand(() => { /* screen-only */ });
        GoBackCommand = new RelayCommand(() =>
        {
            _navigation.NavigateTo<BatchesViewModel>();
        });
    }

    public async Task InitializeAsync(long batchId, string batchInfo)
    {
        _batchId = batchId;
        BatchInfo = batchInfo;
        await LoadAttachmentsAsync();
    }

    private async Task LoadAttachmentsAsync()
    {
        if (_batchId == 0) return;

        IsLoading = true;
        UploadQueue.Clear();

        try
        {
            await using var uow = await _uowFactory.CreateAsync(default);
            var attachments = await uow.Attachments.GetByBatchAsync(_batchId, default);

            foreach (var att in attachments)
            {
                UploadQueue.Add(new AttachmentRow
                {
                    ClaimId = att.ProIdClaim,
                    FileName = att.FileName ?? "Unknown",
                    Status = att.UploadStatus.ToString(),
                    OnlineUrl = att.OnlineUrlPlaintext ?? "—",
                    FailCount = att.AttemptCount
                });
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Error loading attachments: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public sealed class AttachmentRow
    {
        public int ClaimId { get; set; }
        public string FileName { get; set; } = "";
        public string Status { get; set; } = "";
        public string OnlineUrl { get; set; } = "";
        public int FailCount { get; set; }
    }
}

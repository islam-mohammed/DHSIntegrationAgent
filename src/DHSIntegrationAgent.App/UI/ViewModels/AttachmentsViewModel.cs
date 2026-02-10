using System.Collections.ObjectModel;
using DHSIntegrationAgent.App.UI.Mvvm;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class AttachmentsViewModel : ViewModelBase
{
    public ObservableCollection<AttachmentRow> UploadQueue { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand RetrySelectedCommand { get; }

    private AttachmentRow? _selected;
    public AttachmentRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public AttachmentsViewModel()
    {
        RefreshCommand = new RelayCommand(() => { /* screen-only */ });
        RetrySelectedCommand = new RelayCommand(() => { /* screen-only */ });

        UploadQueue.Add(new AttachmentRow
        {
            ClaimId = 12345,
            FileName = "attachment.pdf",
            Status = "Pending",
            OnlineUrl = "—"
        });
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

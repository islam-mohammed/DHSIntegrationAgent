namespace DHSIntegrationAgent.App.UI.Mvvm;

public sealed class PaginationViewModel : ViewModelBase
{
    private int _currentPage = 1;
    private int _totalCount;
    private int _pageSize = 20;

    public IReadOnlyList<int> PageSizeOptions { get; } = new[] { 10, 20, 50, 100 };

    public event EventHandler? PageChanged;

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(HasPrevious));
                OnPropertyChanged(nameof(HasNext));
                OnPropertyChanged(nameof(PageInfo));
                PreviousPageCommand.RaiseCanExecuteChanged();
                NextPageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(HasNext));
                OnPropertyChanged(nameof(PageInfo));
                NextPageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (SetProperty(ref _pageSize, value))
            {
                CurrentPage = 1;
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(PageInfo));
                PageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPrevious => CurrentPage > 1;
    public bool HasNext => CurrentPage < TotalPages;
    public string PageInfo => $"Page {CurrentPage} of {TotalPages}  ({TotalCount:N0} records)";

    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }

    public PaginationViewModel()
    {
        PreviousPageCommand = new RelayCommand(
            () => { CurrentPage--; PageChanged?.Invoke(this, EventArgs.Empty); },
            () => HasPrevious);

        NextPageCommand = new RelayCommand(
            () => { CurrentPage++; PageChanged?.Invoke(this, EventArgs.Empty); },
            () => HasNext);
    }

    public void SetTotalCount(int totalCount)
    {
        TotalCount = totalCount;
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;
    }

    public void Reset()
    {
        _currentPage = 1;
        _totalCount = 0;
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(HasPrevious));
        OnPropertyChanged(nameof(HasNext));
        OnPropertyChanged(nameof(PageInfo));
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
    }
}

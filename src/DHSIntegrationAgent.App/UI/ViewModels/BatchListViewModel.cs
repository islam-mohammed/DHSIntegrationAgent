using System.Collections.ObjectModel;
using DHSIntegrationAgent.App.UI.Models;
using DHSIntegrationAgent.App.UI.Mvvm;

namespace DHSIntegrationAgent.App.UI.ViewModels;

/// <summary>
/// ViewModel for the Batch List view.
/// Manages the list of batches and provides action commands.
/// </summary>
public sealed class BatchListViewModel : ViewModelBase
{
    private ObservableCollection<BatchModel> _batches;
    private BatchModel? _selectedBatch;

    public BatchListViewModel()
    {
        _batches = new ObservableCollection<BatchModel>();
        
        // Load sample data for demonstration
        LoadSampleData();
    }

    public ObservableCollection<BatchModel> Batches
    {
        get => _batches;
        private set => SetProperty(ref _batches, value);
    }

    public BatchModel? SelectedBatch
    {
        get => _selectedBatch;
        set => SetProperty(ref _selectedBatch, value);
    }

    private void LoadSampleData()
    {
        // Sample data for demonstration
        Batches.Add(new BatchModel
        {
            PayerName = "Insurance Company A",
            HISCode = "HIS001",
            TotalClaims = 150,
            BatchDate = DateTime.Now.AddDays(-5),
            BatchNumber = "BATCH-2026-001",
            CreationDate = DateTime.Now.AddDays(-5),
            Status = "Completed",
            UserName = "John Doe"
        });

        Batches.Add(new BatchModel
        {
            PayerName = "Insurance Company B",
            HISCode = "HIS002",
            TotalClaims = 87,
            BatchDate = DateTime.Now.AddDays(-3),
            BatchNumber = "BATCH-2026-002",
            CreationDate = DateTime.Now.AddDays(-3),
            Status = "Processing",
            UserName = "Jane Smith"
        });

        Batches.Add(new BatchModel
        {
            PayerName = "Insurance Company C",
            HISCode = "HIS003",
            TotalClaims = 203,
            BatchDate = DateTime.Now.AddDays(-1),
            BatchNumber = "BATCH-2026-003",
            CreationDate = DateTime.Now.AddDays(-1),
            Status = "Pending",
            UserName = "Bob Johnson"
        });
    }
}

using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Abstractions;

namespace DHSIntegrationAgent.App.UI.ViewModels;

/// <summary>
/// WBS 5.3:
/// Minimal post-login screen that allows the operator to start/stop the in-process worker engine.
/// Full dashboard comes in WBS 5.4.
/// </summary>
public sealed class EngineControlViewModel : ViewModelBase
{
    private readonly IWorkerEngine _engine;

    private bool _isBusy;
    private string? _message;

    public EngineControlViewModel(IWorkerEngine engine)
    {
        _engine = engine;

        StartCommand = new AsyncRelayCommand(StartAsync);
        StopCommand = new AsyncRelayCommand(StopAsync);

        Message = _engine.IsRunning ? "Engine is running." : "Engine is stopped.";
    }

    public bool IsRunning => _engine.IsRunning;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }

    private async Task StartAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            if (!_engine.IsRunning)
                await _engine.StartAsync(CancellationToken.None);

            OnPropertyChanged(nameof(IsRunning));
            Message = "Engine is running.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            if (_engine.IsRunning)
                await _engine.StopAsync(CancellationToken.None);

            OnPropertyChanged(nameof(IsRunning));
            Message = "Engine is stopped.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

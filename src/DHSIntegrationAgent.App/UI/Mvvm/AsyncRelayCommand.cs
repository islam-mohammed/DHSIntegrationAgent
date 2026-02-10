using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DHSIntegrationAgent.App.UI.Mvvm;

/// <summary>
/// Async ICommand that disables itself while executing.
/// Uses fully-qualified System.Windows.Application to avoid collision with DHSIntegrationAgent.Application namespace.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> executeAsync)
        => _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));

    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (_isExecuting) return;

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();

            // WPF command: keep UI context
            await _executeAsync().ConfigureAwait(true);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
    {
        // IMPORTANT: Fully qualify to avoid resolving "Application" to DHSIntegrationAgent.Application namespace.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        else
            dispatcher.Invoke(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
    }
}

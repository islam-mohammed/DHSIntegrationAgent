using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.App.UI.Navigation;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Infrastructure.Providers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DHSIntegrationAgent.App.UI.ViewModels;

/// <summary>
/// WBS 5.3:
/// - Collect Email + Password from the user.
/// - Read GroupID from SQLite AppSettings (via ILoginService / your existing Auth client abstraction).
/// - Call login endpoint (gzip) and validate succeeded==true.
/// - On success: start the worker engine and navigate to EngineControl screen.
/// </summary>
public sealed class LoginViewModel : ViewModelBase
{
    private readonly ILoginService _loginService;
    private readonly IWorkerEngine _workerEngine;
    private readonly INavigationService _navigation;
    private readonly IProviderConfigurationService _providerConfigurationService; 

    private string _email = "";
    private string _password = "";
    private bool _isLoading;
    private string? _error;

    public LoginViewModel(
        ILoginService loginService,
        IWorkerEngine workerEngine,
        INavigationService navigation,
        IProviderConfigurationService providerConfigurationService)
    {
        _loginService = loginService;
        _workerEngine = workerEngine;
        _navigation = navigation;
        _providerConfigurationService = providerConfigurationService;


        LoginCommand = new AsyncRelayCommand(LoginAsync);
    }

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    /// <summary>
    /// Password is bound from PasswordBox via PasswordBoxAssistant.
    /// We clear it after attempt to avoid keeping it in memory.
    /// </summary>
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public AsyncRelayCommand LoginCommand { get; }

    private bool ValidateInputs()
    {
        Error = null;

        if (string.IsNullOrWhiteSpace(Email))
        {
            Error = "Email is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            Error = "Password is required.";
            return false;
        }

        return true;
    }

    private async Task LoginAsync()
    {
        if (IsLoading) return;
        if (!ValidateInputs()) return;

        IsLoading = true;
        Error = null;

        try
        {
            var emailStr = Email.Trim();
            var passwordStr = Password;

            // Important: We do NOT persist sessions.
            // LoginService must read GroupID from SQLite AppSettings and call the backend.
            // Wrap in Task.Run so synchronous IO (like SQLite) doesn't freeze the WPF UI thread spinner.
            var outcome = await Task.Run(() => _loginService.LoginAsync(
                emailStr,
                passwordStr,
                CancellationToken.None));

            if (!outcome.Succeeded)
            {
                Error = outcome.ErrorMessage ?? "Login failed.";
                return;
            }

            // Clear password as soon as we no longer need it.
            Password = "";

            await Task.Run(() => _providerConfigurationService.LoadAsync(CancellationToken.None));

            // Start worker engine after login (WBS 5.3 deliverable).
            if (!_workerEngine.IsRunning)
                await Task.Run(() => _workerEngine.StartAsync(CancellationToken.None));

            _navigation.NavigateTo<DashboardViewModel>();
        }
        catch (Exception ex)
        {
            // PHI-safe: do not log secrets here; UI shows a minimal message.
            Error = $"Unexpected error during login: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            // Clear password even on failure (prevents it sitting in memory + UI).
            Password = "";
        }
    }
}

/// <summary>
/// Abstraction used by the LoginViewModel.
/// IMPORTANT:
/// - If you already have an AuthClient from WBS 2.2, adapt it behind this interface.
/// - This avoids the UI guessing endpoints/JSON parsing.
/// </summary>
public interface ILoginService
{
    Task<LoginOutcome> LoginAsync(string email, string password, CancellationToken ct);
}

public sealed record LoginOutcome(bool Succeeded, string? ErrorMessage);

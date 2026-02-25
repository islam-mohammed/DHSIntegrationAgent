using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.App.UI.Navigation;
using DHSIntegrationAgent.Infrastructure.Security;

namespace DHSIntegrationAgent.App.UI.ViewModels;

/// <summary>
/// WBS 5.2: Two-field setup screen.
/// Persists GroupID + ProviderDhsCode into SQLite.AppSettings (singleton).
/// </summary>
public sealed class SetupViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly INavigationService _navigation;
    private readonly IKeyProtector _protector;

    private string _groupId = "";
    private string _providerDhsCode = "";
    private string _networkUsername = "";
    private string _networkPassword = "";

    private string? _groupIdError;
    private string? _providerDhsCodeError;
    private string? _saveError;
    private bool _isSaving;

    public SetupViewModel(
        ISqliteUnitOfWorkFactory uowFactory,
        INavigationService navigation,
        IKeyProtector protector)
    {
        _uowFactory = uowFactory;
        _navigation = navigation;
        _protector = protector;

        // Save starts enabled; AsyncRelayCommand disables only while executing.
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public string GroupId
    {
        get => _groupId;
        set
        {
            if (SetProperty(ref _groupId, value))
            {
                GroupIdError = null;
                SaveError = null;
            }
        }
    }

    public string ProviderDhsCode
    {
        get => _providerDhsCode;
        set
        {
            if (SetProperty(ref _providerDhsCode, value))
            {
                ProviderDhsCodeError = null;
                SaveError = null;
            }
        }
    }

    public string NetworkUsername
    {
        get => _networkUsername;
        set => SetProperty(ref _networkUsername, value);
    }

    public string NetworkPassword
    {
        get => _networkPassword;
        set => SetProperty(ref _networkPassword, value);
    }

    public string? GroupIdError
    {
        get => _groupIdError;
        private set => SetProperty(ref _groupIdError, value);
    }

    public string? ProviderDhsCodeError
    {
        get => _providerDhsCodeError;
        private set => SetProperty(ref _providerDhsCodeError, value);
    }

    public string? SaveError
    {
        get => _saveError;
        private set => SetProperty(ref _saveError, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set => SetProperty(ref _isSaving, value);
    }

    public AsyncRelayCommand SaveCommand { get; }

    private bool Validate()
    {
        GroupIdError = null;
        ProviderDhsCodeError = null;

        var ok = true;

        var groupId = GroupId.Trim();
        var provider = ProviderDhsCode.Trim();

        if (string.IsNullOrWhiteSpace(groupId))
        {
            GroupIdError = "GroupID is required.";
            ok = false;
        }
        else if (!Guid.TryParse(groupId, out _))
        {
            GroupIdError = "GroupID must be a valid GUID (example: d9a90b95-906e-434c-8ff7-ad1c976c9b50).";
            ok = false;
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            ProviderDhsCodeError = "ProviderDhsCode is required.";
            ok = false;
        }

        return ok;
    }

    private async Task SaveAsync()
    {
        if (IsSaving) return;
        if (!Validate()) return;

        IsSaving = true;
        SaveError = null;

        try
        {
            var groupId = GroupId.Trim();
            var providerDhsCode = ProviderDhsCode.Trim();

            string? netUser = null;
            byte[]? netPassEnc = null;

            if (!string.IsNullOrWhiteSpace(NetworkUsername))
            {
                netUser = NetworkUsername.Trim();
                if (!string.IsNullOrEmpty(NetworkPassword))
                {
                    var bytes = Encoding.UTF8.GetBytes(NetworkPassword);
                    netPassEnc = _protector.Protect(bytes);
                }
            }

            await using var uow = await _uowFactory.CreateAsync(CancellationToken.None);

            await uow.AppSettings.UpdateSetupAsync(
                groupId: groupId,
                providerDhsCode: providerDhsCode,
                networkUsername: netUser,
                networkPasswordEncrypted: netPassEnc,
                utcNow: DateTimeOffset.UtcNow,
                cancellationToken: CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);

            // After Setup, proceed to Login (per PRD journey).
            _navigation.NavigateTo<LoginViewModel>();
        }
        catch (Exception ex)
        {
            SaveError = $"Failed to save setup values: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }
}

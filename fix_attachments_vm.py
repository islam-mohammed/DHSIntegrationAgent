import re

with open('src/DHSIntegrationAgent.App/UI/ViewModels/AttachmentsViewModel.cs', 'r') as f:
    content = f.read()

# Add INavigationService to AttachmentsViewModel constructor properly
content = content.replace('using DHSIntegrationAgent.Application.Persistence;', 'using DHSIntegrationAgent.Application.Persistence;\nusing DHSIntegrationAgent.App.UI.Navigation;')
content = content.replace('private readonly ISqliteUnitOfWorkFactory _uowFactory;', 'private readonly ISqliteUnitOfWorkFactory _uowFactory;\n    private readonly INavigationService _navigation;')
content = content.replace('public AttachmentsViewModel(ISqliteUnitOfWorkFactory uowFactory)', 'public AttachmentsViewModel(ISqliteUnitOfWorkFactory uowFactory, INavigationService navigation)')
content = content.replace('_uowFactory = uowFactory;', '_uowFactory = uowFactory;\n        _navigation = navigation;')

# Fix GoBackCommand to use injected navigation service
content = content.replace("""        GoBackCommand = new RelayCommand(() =>
        {
            var nav = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<DHSIntegrationAgent.App.UI.Navigation.INavigationService>(
                ((App)System.Windows.Application.Current).ServiceHost?.Services);
            nav.NavigateTo<BatchesViewModel>();
        });""", """        GoBackCommand = new RelayCommand(() =>
        {
            _navigation.NavigateTo<BatchesViewModel>();
        });""")

with open('src/DHSIntegrationAgent.App/UI/ViewModels/AttachmentsViewModel.cs', 'w') as f:
    f.write(content)

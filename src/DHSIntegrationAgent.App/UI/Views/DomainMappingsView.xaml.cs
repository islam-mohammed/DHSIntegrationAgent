using System.Windows.Controls;
using DHSIntegrationAgent.App.UI.ViewModels;

namespace DHSIntegrationAgent.App.UI.Views
{
    /// <summary>
    /// Interaction logic for DomainMappingsView.xaml
    /// </summary>
    public partial class DomainMappingsView : UserControl
    {
        private bool _missingMappingsLoaded = false;
        private bool _mappedDomainLoaded = false;

        public DomainMappingsView()
        {
            InitializeComponent();
            Loaded += DomainMappingsView_Loaded;
        }

        private async void DomainMappingsView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // Load data for the first tab when the page loads
            if (DataContext is DomainMappingsViewModel viewModel && !_missingMappingsLoaded)
            {
                _missingMappingsLoaded = true;
                await viewModel.LoadMissingMappingsAsync();
            }
        }

        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not TabControl tabControl || DataContext is not DomainMappingsViewModel viewModel)
                return;

            // Load data for Mapped Domain tab when it's selected for the first time
            if (tabControl.SelectedIndex == 1 && !_mappedDomainLoaded)
            {
                _mappedDomainLoaded = true;
                await viewModel.MappingDomainViewModel.LoadAsync();
            }
        }

        private async void MappingDomainView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // Keep this for backwards compatibility but it won't load data
            // Data loading is now handled by TabControl_SelectionChanged
        }

        private void LoadingOverlay_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {

        }
    }
}

using System.Windows.Controls;

namespace DHSIntegrationAgent.App.UI.Views
{
    /// <summary>
    /// Interaction logic for MappingDomainView.xaml
    /// </summary>
    public partial class MappingDomainView : UserControl
    {
        public MappingDomainView()
        {
            InitializeComponent();
            // Loading is now handled by the parent DomainMappingsView via TabControl_SelectionChanged
            // This prevents database locking issues from simultaneous loads
        }
    }
}

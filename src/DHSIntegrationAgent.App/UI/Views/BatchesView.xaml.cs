using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using DHSIntegrationAgent.App.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DHSIntegrationAgent.App.UI.Views
{
    /// <summary>
    /// Interaction logic for BatchesView.xaml
    /// </summary>
    public partial class BatchesView : UserControl
    {
        public BatchesView()
        {
            InitializeComponent();
            Loaded += BatchesView_Loaded;
        }

        private async void BatchesView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is BatchesViewModel viewModel)
            {
                await viewModel.LoadBatchesAsync();
            }
        }

        private void CreateBatch_Click(object sender, RoutedEventArgs e)
        {
            // Get the service provider from Application
            var app = (App)System.Windows.Application.Current;
            var serviceProvider = app.ServiceHost?.Services;

            if (serviceProvider == null)
            {
                MessageBox.Show("Service provider not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Resolve CreateBatchViewModel from DI
            var createBatchViewModel = serviceProvider.GetRequiredService<CreateBatchViewModel>();

            // Create a new window to host the CreateBatchView
            var createBatchView = new CreateBatchView
            {
                DataContext = createBatchViewModel
            };

            var createBatchWindow = new Window
            {
                Title = "Create New Batch",
                Width = 600,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                Content = createBatchView
            };

            // Show the window as a modal dialog
            createBatchWindow.ShowDialog();
        }
    }
}

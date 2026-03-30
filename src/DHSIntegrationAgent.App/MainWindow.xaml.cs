using System.Windows;
using System.ComponentModel;
using DHSIntegrationAgent.App.UI.ViewModels;
using Microsoft.Extensions.Logging;
using DHSIntegrationAgent.Application.Abstractions;

namespace DHSIntegrationAgent.App
{
    public partial class MainWindow : Window
    {
        private readonly ILogger<MainWindow> _logger;
        private readonly IWorkerEngine _workerEngine;
        private readonly IBatchRegistry _batchRegistry;

        public MainWindow(ILogger<MainWindow> logger, ShellViewModel shellViewModel, IWorkerEngine workerEngine, IBatchRegistry batchRegistry)
        {
            InitializeComponent();

            _logger = logger;
            _workerEngine = workerEngine;
            _batchRegistry = batchRegistry;
            DataContext = shellViewModel;

            _logger.LogInformation("MainWindow created via DI and bound to ShellViewModel.");

            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_workerEngine.IsRunning && _batchRegistry.HasRegisteredBatches)
            {
                var result = MessageBox.Show(
                    "Background tasks are still running. Are you sure you want to exit?",
                    "Confirm Exit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}

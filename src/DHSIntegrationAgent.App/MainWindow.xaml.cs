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

        public MainWindow(ILogger<MainWindow> logger, ShellViewModel shellViewModel, IWorkerEngine workerEngine)
        {
            InitializeComponent();

            _logger = logger;
            _workerEngine = workerEngine;
            DataContext = shellViewModel;

            _logger.LogInformation("MainWindow created via DI and bound to ShellViewModel.");

            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_workerEngine.IsRunning)
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

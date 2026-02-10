using System.Windows;
using DHSIntegrationAgent.App.UI.ViewModels;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.App;

public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(ILogger<MainWindow> logger, ShellViewModel shellViewModel)
    {
        InitializeComponent();

        _logger = logger;
        DataContext = shellViewModel;

        _logger.LogInformation("MainWindow created via DI and bound to ShellViewModel.");
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {

    }
}

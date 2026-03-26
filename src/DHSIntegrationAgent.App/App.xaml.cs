using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DHSIntegrationAgent.App.DependencyInjection;
using DHSIntegrationAgent.Bootstrapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DHSIntegrationAgent.App
{
    public partial class App : System.Windows.Application
    {
        private IHost? _host;

        /// <summary>
        /// Gets the current IHost for accessing services via DI.
        /// </summary>
        public IHost? ServiceHost => _host;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _host = CreateHostBuilder().Build();
            await _host.StartAsync();

            // Route to Setup or Login BEFORE showing MainWindow.
            var router = _host.Services.GetRequiredService<DHSIntegrationAgent.App.UI.Navigation.IStartupRouter>();
            await router.RouteAsync(CancellationToken.None);

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host is not null)
            {
                try
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(5));
                }
                finally
                {
                    _host.Dispose();
                    _host = null;
                }
            }

            base.OnExit(e);
        }

        private static IHostBuilder CreateHostBuilder()
        {
            return Host.CreateDefaultBuilder()
                .UseSerilog((context, services, configuration) =>
                {
                    var appOptions = context.Configuration.GetSection("App").Get<DHSIntegrationAgent.Application.Configuration.AppOptions>();
                    var logFolder = string.IsNullOrWhiteSpace(appOptions?.LogFolder)
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DHSIntegrationAgent", "Logs")
                        : appOptions.LogFolder;

                    configuration
                        .ReadFrom.Configuration(context.Configuration)
                        .Enrich.FromLogContext()
                        .WriteTo.Async(a => a.File(
                            Path.Combine(logFolder, "log-.txt"),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 31));
                })
                .ConfigureAppConfiguration((context, config) =>
                {
                    var basePath = AppContext.BaseDirectory;
                    config.SetBasePath(basePath);

                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);

                    if (context.HostingEnvironment.IsDevelopment())
                    {
                        config.AddUserSecrets<App>();
                    }

                    config.AddEnvironmentVariables(prefix: "DHSAGENT_");
                })
                .ConfigureServices((context, services) =>
                {
                    // Register core agent (App + Infrastructure + Workers)
                    services.AddDhsIntegrationAgent(context.Configuration);

                    // Register WPF UI
                    services.AddDhsWpfUi();
                    services.AddSingleton<MainWindow>();

                });
        }
    }
}

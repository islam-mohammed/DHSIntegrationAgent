using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DHSIntegrationAgent.Tests.Integration;

public sealed class AppSettingsPersistenceTests
{
    [Fact]
    public async Task Can_Update_And_Read_NetworkCredentials()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dhs_agent_test_appsettings_{Guid.NewGuid():N}.db");
        ServiceProvider? sp = null;

        try
        {
            var services = new ServiceCollection();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["App:DatabasePath"] = dbPath,
                    ["Api:BaseUrl"] = "https://example.invalid",
                    ["Api:TimeoutSeconds"] = "30"
                })
                .Build();

            services.AddDhsApplication(config);
            services.AddDhsInfrastructure(config);
            sp = services.BuildServiceProvider();

            // Migrate
            var migrator = sp.GetRequiredService<ISqliteMigrator>();
            await migrator.MigrateAsync(CancellationToken.None);

            var uowFactory = sp.GetRequiredService<ISqliteUnitOfWorkFactory>();

            // 2. Update Setup with Network Creds
            var now = DateTimeOffset.UtcNow;
            byte[] encryptedPass = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };

            await using (var uow = await uowFactory.CreateAsync(CancellationToken.None))
            {
                await uow.AppSettings.UpdateSetupAsync(
                    "my-group",
                    "my-provider",
                    "domain\\user",
                    encryptedPass,
                    now,
                    CancellationToken.None);
                await uow.CommitAsync(CancellationToken.None);
            }

            // 3. Read back
            await using (var uow = await uowFactory.CreateAsync(CancellationToken.None))
            {
                var settings = await uow.AppSettings.GetAsync(CancellationToken.None);
                Assert.Equal("my-group", settings.GroupId);
                Assert.Equal("my-provider", settings.ProviderDhsCode);
                Assert.Equal("domain\\user", settings.NetworkUsername);
                Assert.Equal(encryptedPass, settings.NetworkPasswordEncrypted);
            }
        }
        finally
        {
            sp?.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}

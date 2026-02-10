using DHSIntegrationAgent.Contracts.Persistence;
﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Adapters;
using DHSIntegrationAgent.Application;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Application.Security;
using DHSIntegrationAgent.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DHSIntegrationAgent.Tests.Integration;

public sealed class ProviderDbFactory_Wbs3_1_Tests
{
    [Fact]
    public async Task CreateAsync_decrypts_connection_string_and_applies_secure_defaults()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dhs_agent_test_{Guid.NewGuid():N}.db");
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
            services.AddDhsAdapters(config);

            sp = services.BuildServiceProvider();

            // Ensure schema exists
            var migrator = sp.GetRequiredService<ISqliteMigrator>();
            await migrator.MigrateAsync(CancellationToken.None);

            // Initialize encryption key material (normally done by hosted service)
            foreach (var hs in sp.GetServices<IHostedService>())
            {
                if (hs.GetType().Name == "CryptoKeyInitializationHostedService")
                    await hs.StartAsync(CancellationToken.None);
            }

            var encryptor = sp.GetRequiredService<IColumnEncryptor>();
            var uowFactory = sp.GetRequiredService<ISqliteUnitOfWorkFactory>();

            var providerDhsCode = "54455";
            var providerCode = "DEFAULT";

            // Connection string WITHOUT explicit Encrypt/TrustServerCertificate to ensure defaults apply.
            var plaintextConnString = "Server=localhost;Database=ProviderDb;User Id=sa;Password=Strong!Passw0rd;";
            var encrypted = await encryptor.EncryptAsync(Encoding.UTF8.GetBytes(plaintextConnString), CancellationToken.None);

            await using (var uow = await uowFactory.CreateAsync(CancellationToken.None))
            {
                await uow.ProviderProfiles.UpsertAsync(new ProviderProfileRow(
                    ProviderCode: providerCode,
                    ProviderDhsCode: providerDhsCode,
                    DbEngine: "sqlserver",
                    IntegrationType: "Tables",
                    EncryptedConnectionString: encrypted,
                    EncryptionKeyId: null,
                    IsActive: true,
                    CreatedUtc: DateTimeOffset.UtcNow,
                    UpdatedUtc: DateTimeOffset.UtcNow), CancellationToken.None);

                await uow.CommitAsync(CancellationToken.None);
            }

            var sut = sp.GetRequiredService<IProviderDbFactory>();

            // Act (no network): create only
            await using var handle = await sut.CreateAsync(providerDhsCode, CancellationToken.None);

            Assert.Equal(providerDhsCode, handle.ProviderDhsCode);
            Assert.Equal("sqlserver", handle.DbEngine, ignoreCase: true);

            var cs = handle.Connection.ConnectionString;
            Assert.Contains("Encrypt=True", cs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("TrustServerCertificate=False", cs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Application Name=DHSIntegrationAgent", cs, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { sp?.Dispose(); } catch { }
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task CreateAsync_throws_if_profile_missing()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dhs_agent_test_{Guid.NewGuid():N}.db");
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
            services.AddDhsAdapters(config);

            sp = services.BuildServiceProvider();

            var migrator = sp.GetRequiredService<ISqliteMigrator>();
            await migrator.MigrateAsync(CancellationToken.None);

            foreach (var hs in sp.GetServices<IHostedService>())
            {
                if (hs.GetType().Name == "CryptoKeyInitializationHostedService")
                    await hs.StartAsync(CancellationToken.None);
            }

            var sut = sp.GetRequiredService<IProviderDbFactory>();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sut.CreateAsync("missing", CancellationToken.None));
        }
        finally
        {
            try { sp?.Dispose(); } catch { }
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }
}

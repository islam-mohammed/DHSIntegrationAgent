using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.Adapters;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Contracts.Providers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using DHSIntegrationAgent.Application.Providers;
using System.Data.Common;
using System.Text.Json.Nodes;
using DHSIntegrationAgent.Contracts.Adapters;

namespace DHSIntegrationAgent.Tests.Integration;

public class ProviderTablesAdapterTests
{
    [Fact]
    public async Task GetClaimBundlesRawBatchAsync_ShouldHandleEmptyList()
    {
        // Arrange
        var dbFactory = new MockProviderDbFactory();
        var uowFactory = new MockSqliteUnitOfWorkFactory();
        var sut = new ProviderTablesAdapter(dbFactory, uowFactory);

        // Act
        var result = await sut.GetClaimBundlesRawBatchAsync("123", Array.Empty<int>(), CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    private class MockProviderDbFactory : IProviderDbFactory
    {
        public Task<ProviderDbHandle> CreateAsync(string providerDhsCode, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ProviderDbHandle> OpenAsync(string providerDhsCode, CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }

    private class MockSqliteUnitOfWorkFactory : ISqliteUnitOfWorkFactory
    {
        public Task<ISqliteUnitOfWork> CreateAsync(CancellationToken ct)
            => throw new NotImplementedException();
    }
}

using System.Reflection;
using System.Text.Json.Nodes;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Workers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DHSIntegrationAgent.Tests.Unit.Workers;

public class DispatchServiceMappingTests
{
    [Fact]
    public void TryGetMapping_ShouldSucceed_WhenUsingDomainTableId_EvenIfNameIsMessy()
    {
        // Arrange
        // We use reflection because TryGetMapping is private.
        // We don't need real dependencies for this specific logic test.
        var service = (DispatchService)Activator.CreateInstance(typeof(DispatchService), new object[] { null!, null!, null!, null! })!;

        var method = typeof(DispatchService).GetMethod("TryGetMapping", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var obj = new JsonObject
        {
            ["nationality"] = "JORDANIAN"
        };

        // This simulates the "messy" dictionary from the image where DomainName == SourceValue
        var mappingLookup = new Dictionary<(int DomainTableId, string SourceValue), ApprovedDomainMappingRow>
        {
            [(5, "jordanian")] = new ApprovedDomainMappingRow(
                DomainMappingId: 153,
                ProviderDhsCode: "C-123",
                DomainName: "jordanian", // Messy name
                DomainTableId: 5, // Correct ID for Country
                SourceValue: "JORDANIAN",
                TargetValue: "1",
                DiscoveredUtc: DateTimeOffset.UtcNow,
                LastPostedUtc: null,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                Notes: null,
                ProviderDomainCode: null,
                IsDefault: false,
                CodeValue: "JOR",
                DisplayValue: "Jordan")
        };

        // Act
        var parameters = new object[] { obj, "nationality", 5, mappingLookup, null! };
        var result = (bool)method.Invoke(service, parameters)!;
        var mapping = (ApprovedDomainMappingRow)parameters[4];

        // Assert
        Assert.True(result);
        Assert.NotNull(mapping);
        Assert.Equal("1", mapping.TargetValue);
        Assert.Equal("Jordan", mapping.DisplayValue);
    }
}

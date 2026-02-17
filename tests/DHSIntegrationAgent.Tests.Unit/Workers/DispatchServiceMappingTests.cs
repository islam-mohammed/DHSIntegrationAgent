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
    public void EnrichSection_ShouldSucceed_WhenUsingDomainTableId_EvenIfNameIsMessy()
    {
        // Arrange
        // We use reflection because EnrichSection is private.
        // We don't need real dependencies for this specific logic test.
        var service = (DispatchService)Activator.CreateInstance(typeof(DispatchService), new object[] { null!, null!, null!, null! })!;

        var method = typeof(DispatchService).GetMethod("EnrichSection", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var obj = new JsonObject
        {
            ["nationality"] = "JORDANIAN"
        };

        var sectionLookup = new Dictionary<string, List<(string TargetField, string DomainName, string SourceField)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["nationality"] = new List<(string TargetField, string DomainName, string SourceField)>
            {
                ("fK_Nationality_ID", "Country", "nationality")
            }
        };

        // This simulates the "messy" dictionary where DomainName == SourceValue
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
        var parameters = new object[] { obj, sectionLookup, mappingLookup };
        method.Invoke(service, parameters);

        // Assert
        Assert.NotNull(obj["fK_Nationality_ID"]);
        var enriched = obj["fK_Nationality_ID"]!.AsObject();
        Assert.Equal("1", enriched["id"]!.ToString());
        Assert.Equal("Jordan", enriched["name"]!.ToString());
    }
}

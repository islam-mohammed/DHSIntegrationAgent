using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Claims;
using DHSIntegrationAgent.Domain.Claims;
using Xunit;

namespace DHSIntegrationAgent.Tests.Unit.Adapters;

public class ClaimBundleBuilderTests
{
    [Fact]
    public void Build_ShouldIncludeDoctorDetails()
    {
        // Arrange
        var builder = new ClaimBundleBuilder();
        var header = new JsonObject
        {
            ["proIdClaim"] = 123,
            ["companyCode"] = "COMP1",
            ["invoiceDate"] = "2023-10-01"
        };
        var doctor = new JsonObject
        {
            ["doctorName"] = "Dr. Smith",
            ["specialty"] = "General"
        };
        var parts = new CanonicalClaimParts(header, DoctorDetails: doctor);

        // Act
        var result = builder.Build(parts, "COMP1");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Bundle.DoctorDetails);
        Assert.Equal("Dr. Smith", result.Bundle.DoctorDetails["doctorName"]?.ToString());
        Assert.Equal(123, result.Bundle.DoctorDetails["proIdClaim"]?.GetValue<int>());
    }

    [Fact]
    public void Build_ShouldHandleNullDoctorDetails()
    {
        // Arrange
        var builder = new ClaimBundleBuilder();
        var header = new JsonObject
        {
            ["proIdClaim"] = 123,
            ["companyCode"] = "COMP1",
            ["invoiceDate"] = "2023-10-01"
        };
        var parts = new CanonicalClaimParts(header, DoctorDetails: null);

        // Act
        var result = builder.Build(parts, "COMP1");

        // Assert
        Assert.True(result.Succeeded);
        Assert.Null(result.Bundle.DoctorDetails);
    }

    [Fact]
    public void ToJsonString_ShouldIncludeDoctorDetails()
    {
        // Arrange
        var header = new JsonObject { ["proIdClaim"] = 123 };
        var doctor = new JsonObject { ["doctorName"] = "Dr. Smith" };
        var bundle = new ClaimBundle(header, doctorDetails: doctor);

        // Act
        var json = bundle.ToJsonString();

        // Assert
        Assert.Contains("\"doctorDetails\":{\"doctorName\":\"Dr. Smith\"}", json);
    }
}

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
        var doctorArr = new JsonArray { doctor };
        var parts = new CanonicalClaimParts(header, DoctorDetails: doctorArr);

        // Act
        var result = builder.Build(parts, "COMP1");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.Bundle.DhsDoctors);
        var firstDoctor = result.Bundle.DhsDoctors[0] as JsonObject;
        Assert.NotNull(firstDoctor);
        Assert.Equal("Dr. Smith", firstDoctor["doctorName"]?.ToString());
        Assert.Equal(123, firstDoctor["proIdClaim"]?.GetValue<int>());
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
        Assert.Empty(result.Bundle.DhsDoctors);
    }

    [Fact]
    public void ToJsonString_ShouldIncludeDoctorDetails()
    {
        // Arrange
        var header = new JsonObject { ["proIdClaim"] = 123 };
        var doctor = new JsonObject { ["doctorName"] = "Dr. Smith" };
        var doctorArr = new JsonArray { doctor };
        var bundle = new ClaimBundle(header, dhsDoctors: doctorArr);

        // Act
        var json = bundle.ToJsonString();

        // Assert
        Assert.Contains("\"dhsDoctors\":[{\"doctorName\":\"Dr. Smith\"", json);
    }
}

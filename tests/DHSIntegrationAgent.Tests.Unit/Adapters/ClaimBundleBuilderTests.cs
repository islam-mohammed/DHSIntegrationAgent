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
        var parts = new CanonicalClaimParts(header, DhsDoctors: doctorArr);

        // Act
        var result = builder.Build(parts, "COMP1");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.Bundle.DhsDoctors);
        var firstDoctor = result.Bundle.DhsDoctors[0] as JsonObject;
        Assert.NotNull(firstDoctor);
        Assert.Equal("Dr. Smith", firstDoctor["doctorName"]?.ToString());
        Assert.False(firstDoctor.ContainsKey("proIdClaim"));
        Assert.False(firstDoctor.ContainsKey("proidclaim"));
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
        var parts = new CanonicalClaimParts(header, DhsDoctors: null);

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

    [Fact]
    public void Build_ShouldUseLowercaseProidclaimAsStringForServiceDetails()
    {
        // Arrange
        var builder = new ClaimBundleBuilder();
        var header = new JsonObject
        {
            ["proIdClaim"] = 123,
            ["companyCode"] = "COMP1",
            ["invoiceDate"] = "2023-10-01"
        };
        var service = new JsonObject
        {
            ["serviceCode"] = "S1"
        };
        var diagnosis = new JsonObject
        {
            ["diagnosisCode"] = "D1"
        };
        var parts = new CanonicalClaimParts(
            header,
            ServiceDetails: new JsonArray { service },
            DiagnosisDetails: new JsonArray { diagnosis });

        // Act
        var result = builder.Build(parts, "COMP1");

        // Assert
        Assert.True(result.Succeeded);

        var firstService = result.Bundle.ServiceDetails[0] as JsonObject;
        Assert.NotNull(firstService);
        Assert.True(firstService.ContainsKey("proidclaim"));
        Assert.False(firstService.ContainsKey("proIdClaim"));
        Assert.Equal("123", firstService["proidclaim"]?.ToString());

        var firstDiagnosis = result.Bundle.DiagnosisDetails[0] as JsonObject;
        Assert.NotNull(firstDiagnosis);
        Assert.False(firstDiagnosis.ContainsKey("proIdClaim"));
        Assert.False(firstDiagnosis.ContainsKey("proidclaim"));
    }

    [Fact]
    public void Build_ShouldFormatDiagnosisDateAsDateOnly()
    {
        // Arrange
        var builder = new ClaimBundleBuilder();
        var header = new JsonObject
        {
            ["proIdClaim"] = 123,
            ["companyCode"] = "COMP1",
            ["invoiceDate"] = "2023-10-01"
        };
        var diagnosis1 = new JsonObject
        {
            ["diagnosisCode"] = "D1",
            ["DiagnosisDate"] = "2023-10-27T10:00:00Z"
        };
        var diagnosis2 = new JsonObject
        {
            ["diagnosisCode"] = "D2",
            ["diagnosis_Date"] = "2023-10-28 14:00:00"
        };
        var diagnosis3 = new JsonObject
        {
            ["diagnosisCode"] = "D3",
            ["diagnosisDate"] = new DateTime(2023, 10, 29, 10, 0, 0) // Test DateTime object
        };
        var parts = new CanonicalClaimParts(header, DiagnosisDetails: new JsonArray { diagnosis1, diagnosis2, diagnosis3 });

        // Act
        var result = builder.Build(parts, "COMP1");

        // Assert
        Assert.True(result.Succeeded);
        var firstDiag = result.Bundle.DiagnosisDetails[0] as JsonObject;
        Assert.Equal("2023-10-27", firstDiag?["DiagnosisDate"]?.ToString());
        Assert.False(firstDiag?.ContainsKey("diagnosisDate"));
        Assert.False(firstDiag?.ContainsKey("diagnosis_Date"));

        var secondDiag = result.Bundle.DiagnosisDetails[1] as JsonObject;
        Assert.Equal("2023-10-28", secondDiag?["DiagnosisDate"]?.ToString());
        Assert.False(secondDiag?.ContainsKey("diagnosisDate"));
        Assert.False(secondDiag?.ContainsKey("diagnosis_Date"));

        var thirdDiag = result.Bundle.DiagnosisDetails[2] as JsonObject;
        Assert.Equal("2023-10-29", thirdDiag?["DiagnosisDate"]?.ToString());
        Assert.False(thirdDiag?.ContainsKey("diagnosisDate"));
        Assert.False(thirdDiag?.ContainsKey("diagnosis_Date"));
    }
}

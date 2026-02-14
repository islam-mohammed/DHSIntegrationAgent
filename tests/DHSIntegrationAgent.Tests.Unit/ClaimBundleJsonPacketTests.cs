using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Claims;
using Xunit;

namespace DHSIntegrationAgent.Tests.Unit;

public class ClaimBundleJsonPacketTests
{
    [Fact]
    public void ToJsonArray_SerializesJsonNodesCorrectly()
    {
        // Arrange
        var nodes = new List<JsonNode>
        {
            new JsonObject { ["id"] = 1, ["name"] = "Claim 1" },
            new JsonObject { ["id"] = 2, ["name"] = "Claim 2" }
        };

        // Act
        var json = ClaimBundleJsonPacket.ToJsonArray(nodes);

        // Assert
        Assert.Equal("[{\"id\":1,\"name\":\"Claim 1\"},{\"id\":2,\"name\":\"Claim 2\"}]", json);
    }
}

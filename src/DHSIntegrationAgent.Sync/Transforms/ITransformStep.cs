using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Sync.Transforms;

public interface ITransformStep
{
    string Name { get; }
    void Apply(JsonObject row, string columnName, Dictionary<string, object> parameters);
}

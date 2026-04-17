using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Adapters;

public interface ISchemaMapperService
{
    JsonObject MapRow(string category, JsonObject rawRow, JsonObject? parsedConfig);
}

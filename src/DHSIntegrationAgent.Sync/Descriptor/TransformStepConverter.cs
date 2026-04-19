using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DHSIntegrationAgent.Sync.Descriptor;

public class TransformStepConverter : JsonConverter<TransformStepDefinition>
{
    public override TransformStepDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new TransformStepDefinition { Name = reader.GetString()! };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var name = root.GetProperty("name").GetString()!;
            var parameters = new Dictionary<string, object>();

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name != "name")
                {
                    // Basic mapping for simple types
                    parameters[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString()!,
                        JsonValueKind.Number => prop.Value.GetInt32(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.ToString()
                    };
                }
            }
            return new TransformStepDefinition { Name = name, Parameters = parameters };
        }

        throw new JsonException("Expected string or object for transform step");
    }

    public override void Write(Utf8JsonWriter writer, TransformStepDefinition value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}

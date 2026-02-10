using System.Data.Common;
using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Adapters.Tables;

internal static class DbReaderJsonExtensions
{
    public static JsonObject ReadRowAsJsonObject(this DbDataReader reader)
    {
        var obj = new JsonObject();

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);

            if (reader.IsDBNull(i))
            {
                obj[name] = null;
                continue;
            }

            var value = reader.GetValue(i);

            // Keep conversions conservative and deterministic.
            obj[name] = value switch
            {
                bool b => b,
                byte bt => bt,
                short s => s,
                int n => n,
                long l => l,
                float f => f,
                double d => d,
                decimal dc => dc,
                DateTime dt => dt,                 // JsonNode will serialize as ISO string via System.Text.Json
                DateTimeOffset dto => dto,
                Guid g => g.ToString(),
                byte[] bytes => Convert.ToBase64String(bytes),
                _ => value.ToString()
            };
        }

        return obj;
    }

    public static async Task<JsonArray> ReadAllRowsAsJsonArrayAsync(this DbDataReader reader, CancellationToken ct)
    {
        var arr = new JsonArray();
        while (await reader.ReadAsync(ct))
        {
            arr.Add(reader.ReadRowAsJsonObject());
        }
        return arr;
    }
}

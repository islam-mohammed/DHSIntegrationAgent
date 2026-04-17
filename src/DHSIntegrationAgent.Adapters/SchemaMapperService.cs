using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Adapters;

public sealed class SchemaMapperService : ISchemaMapperService
{
    public JsonObject MapRow(string category, JsonObject rawRow, JsonObject? parsedConfig)
    {
        if (parsedConfig == null)
        {
            return rawRow;
        }

        try
        {
            // Looking for category mapping, e.g., "Header", "Services", "Diagnosis"
            if (!parsedConfig.TryGetPropertyValue(category, out var categoryNode) || categoryNode is not JsonObject categoryConfig)
            {
                return rawRow;
            }

            if (!categoryConfig.TryGetPropertyValue("Fields", out var fieldsNode) || fieldsNode is not JsonObject fieldsMap)
            {
                return rawRow;
            }

            // Mutate the original row directly for performance, as DbDataReader generates a fresh instance per row.
            foreach (var kvp in fieldsMap)
            {
                var canonicalKey = kvp.Key;
                var vendorKey = kvp.Value?.ToString();

                if (!string.IsNullOrWhiteSpace(vendorKey))
                {
                    // Find the vendor key case-insensitively in the rawRow
                    JsonNode? vendorValue = null;
                    string? foundKey = null;

                    foreach (var rawKvp in rawRow)
                    {
                        if (string.Equals(rawKvp.Key, vendorKey, StringComparison.OrdinalIgnoreCase))
                        {
                            foundKey = rawKvp.Key;
                            // Detach the node from the parent JsonObject before re-attaching it to the new key
                            // to avoid System.InvalidOperationException: The node already has a parent.
                            if (rawKvp.Value != null)
                            {
                                vendorValue = rawKvp.Value;
                            }
                            break;
                        }
                    }

                    if (foundKey != null)
                    {
                        rawRow.Remove(foundKey);
                        // Assign the mapped value to the new canonical key.
                        rawRow[canonicalKey] = vendorValue;
                    }
                }
            }

            return rawRow;
        }
        catch
        {
            return rawRow;
        }
    }
}

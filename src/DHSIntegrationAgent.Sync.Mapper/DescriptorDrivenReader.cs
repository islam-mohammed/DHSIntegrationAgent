using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Sync.Mapper;

public static class DescriptorDrivenReader
{
    // Reads one row into a JsonObject keyed by CANONICAL names.
    // Columns absent from the manifest are dropped.
    // isServiceEntity: when true, ProIdClaim is coerced to string (invariant §18.2).
    public static JsonObject ReadRow(
        DbDataReader reader,
        ColumnManifest manifest,
        TypeCoercionMap typeMap,
        bool isServiceEntity = false,
        ILogger? logger = null)
    {
        var obj = new JsonObject();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.IsDBNull(i)) continue;

            var sourceName = reader.GetName(i);
            var canonical  = manifest.ResolveCanonical(sourceName);
            if (canonical is null) continue;

            // Descriptor-declared type takes precedence; TypeCoercionMap is the fallback.
            var clrType = manifest.GetTargetType(canonical) ?? typeMap.GetTargetType(canonical);

            // Service entity invariant: proidclaim must be string regardless of declared type.
            if (isServiceEntity && CanonicalSchema.ServiceProIdClaimIsString &&
                canonical.Equals(CanonicalSchema.ProIdClaim, StringComparison.OrdinalIgnoreCase))
            {
                clrType = typeof(string);
            }

            obj[canonical] = CoerceToJsonNode(reader.GetValue(i), clrType, canonical, logger);
        }
        return obj;
    }

    public static async Task<JsonArray> ReadAllRowsAsync(
        DbDataReader reader,
        ColumnManifest manifest,
        TypeCoercionMap typeMap,
        CancellationToken ct,
        bool isServiceEntity = false,
        ILogger? logger = null)
    {
        var arr = new JsonArray();
        while (await reader.ReadAsync(ct))
            arr.Add(ReadRow(reader, manifest, typeMap, isServiceEntity, logger));
        return arr;
    }

    private static JsonNode? CoerceToJsonNode(object value, Type? clrType, string canonicalName, ILogger? logger)
    {
        if (value is null || value == DBNull.Value) return null;

        if (clrType is not null)
        {
            try
            {
                if (clrType == typeof(int))      return JsonValue.Create(Convert.ToInt32(value));
                if (clrType == typeof(long))     return JsonValue.Create(Convert.ToInt64(value));
                if (clrType == typeof(decimal))  return JsonValue.Create(Convert.ToDecimal(value));
                if (clrType == typeof(bool))     return JsonValue.Create(Convert.ToBoolean(value));
                if (clrType == typeof(DateTime)) return JsonValue.Create(Convert.ToDateTime(value));
                if (clrType == typeof(string))   return JsonValue.Create(value.ToString());
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    "Type coercion failed for field '{Field}': could not convert value '{Value}' (runtime type {RuntimeType}) to declared type {DeclaredType}. Falling back to native DB type. {Error}",
                    canonicalName, value, value.GetType().Name, clrType.Name, ex.Message);
            }
        }

        return value switch
        {
            bool b             => JsonValue.Create(b),
            byte bt            => JsonValue.Create((int)bt),
            short s            => JsonValue.Create((int)s),
            int n              => JsonValue.Create(n),
            long l             => JsonValue.Create(l),
            float f            => JsonValue.Create(f),
            double d           => JsonValue.Create(d),
            decimal dc         => JsonValue.Create(dc),
            DateTime dt        => JsonValue.Create(dt),
            DateTimeOffset dto => JsonValue.Create(dto),
            Guid g             => JsonValue.Create(g.ToString()),
            byte[] bytes       => JsonValue.Create(Convert.ToBase64String(bytes)),
            _                  => JsonValue.Create(value.ToString())
        };
    }
}

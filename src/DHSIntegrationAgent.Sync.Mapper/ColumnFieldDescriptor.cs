using System.Text.Json.Serialization;

namespace DHSIntegrationAgent.Sync.Mapper;

// Declares both the vendor DB source column name and the target CLR type for one manifest entry.
// A null instance in the manifest dictionary means "skip this column — do not SELECT it".
public sealed class ColumnFieldDescriptor
{
    // Vendor DB column name. Null means skip (same semantic as a null entry).
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    // Target CLR type as a lower-case string: "string" | "int" | "decimal" | "datetime" | "bool"
    // Null means fall back to TypeCoercionMap, then native DB type.
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    // Resolves the declared type string to a CLR Type. Returns null when Type is absent or unrecognised.
    [JsonIgnore]
    public Type? ClrType => Type?.ToLowerInvariant() switch
    {
        "string"   => typeof(string),
        "int"      => typeof(int),
        "decimal"  => typeof(decimal),
        "datetime" => typeof(DateTime),
        "bool"     => typeof(bool),
        _          => null
    };
}

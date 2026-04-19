using System.Text.Json.Serialization;

namespace DHSIntegrationAgent.Sync.Descriptor;

public sealed record VendorDescriptor
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("providerCode")]
    public string ProviderCode { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public VendorSource Source { get; init; } = new();

    [JsonPropertyName("transforms")]
    public Dictionary<string, List<TransformRule>> Transforms { get; init; } = new();

    [JsonPropertyName("childRebind")]
    public List<ChildRebindRule> ChildRebind { get; init; } = new();

    [JsonPropertyName("postLoadRules")]
    public List<PostLoadRule> PostLoadRules { get; init; } = new();
}

public sealed record VendorSource
{
    [JsonPropertyName("engine")]
    public string Engine { get; init; } = "sqlserver";

    [JsonPropertyName("dateColumn")]
    public string DateColumn { get; init; } = string.Empty;

    [JsonPropertyName("companyCodeColumn")]
    public string CompanyCodeColumn { get; init; } = string.Empty;

    [JsonPropertyName("tables")]
    public Dictionary<string, TableDefinition> Tables { get; init; } = new();
}

public sealed record TableDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("keyColumn")]
    public string? KeyColumn { get; init; }

    [JsonPropertyName("parentKeyColumn")]
    public string? ParentKeyColumn { get; init; }
}

public sealed record TransformRule
{
    [JsonPropertyName("column")]
    public string Column { get; init; } = string.Empty;

    [JsonPropertyName("steps")]
    public List<TransformStepDefinition> Steps { get; init; } = new();
}

// Custom converter needed for mixed array (string OR object) in steps
[JsonConverter(typeof(TransformStepConverter))]
public sealed record TransformStepDefinition
{
    public string Name { get; init; } = string.Empty;
    public Dictionary<string, object> Parameters { get; init; } = new();
}

public sealed record ChildRebindRule
{
    [JsonPropertyName("child")]
    public string Child { get; init; } = string.Empty;

    [JsonPropertyName("strategy")]
    public string Strategy { get; init; } = string.Empty;
}

public sealed record PostLoadRule
{
    [JsonPropertyName("rule")]
    public string Rule { get; init; } = string.Empty;
}

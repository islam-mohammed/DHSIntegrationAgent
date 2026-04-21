using System.Text.Json;
using System.Text.Json.Serialization;
using DHSIntegrationAgent.Sync.Mapper;

namespace DHSIntegrationAgent.Sync.Pipeline;

// Full parsed model of the JSON blob stored in VendorDescriptor.DescriptorJson.
public sealed class VendorDescriptor
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static VendorDescriptor Deserialize(string json)
        => JsonSerializer.Deserialize<VendorDescriptor>(json, _opts)
           ?? throw new InvalidOperationException("Failed to deserialize VendorDescriptor.");

    [JsonPropertyName("vendorId")]
    public string VendorId { get; init; } = "";

    [JsonPropertyName("dbEngine")]
    public string DbEngine { get; init; } = "sqlserver";

    // "tableToTable" | "viewToTable" | "flatView" | "customSql"
    [JsonPropertyName("topology")]
    public string Topology { get; init; } = "tableToTable";

    [JsonPropertyName("sources")]
    public Dictionary<string, string> Sources { get; init; } = new();

    [JsonPropertyName("customSql")]
    public Dictionary<string, string>? CustomSql { get; init; }

    [JsonPropertyName("filter")]
    public FilterConfig Filter { get; init; } = new();

    [JsonPropertyName("paging")]
    public PagingConfig Paging { get; init; } = new();

    // Key = entity name, Value = (canonical → ColumnFieldDescriptor) map; null value = skipColumn.
    [JsonPropertyName("columnManifests")]
    public Dictionary<string, Dictionary<string, ColumnFieldDescriptor?>> ColumnManifests { get; init; } = new();

    [JsonPropertyName("flatView")]
    public FlatViewConfig? FlatView { get; init; }

    [JsonPropertyName("grouping")]
    public GroupingConfig Grouping { get; init; } = new();

    [JsonPropertyName("postLoadRules")]
    public List<PostLoadRuleConfig> PostLoadRules { get; init; } = new();

    [JsonPropertyName("distinctEntities")]
    public List<string>? DistinctEntities { get; init; }

    [JsonPropertyName("validationGates")]
    public ValidationGatesConfig ValidationGates { get; init; } = new();
}

public sealed class FilterConfig
{
    [JsonPropertyName("claimKeyColumn")]
    public string ClaimKeyColumn { get; init; } = "ProIdClaim";

    [JsonPropertyName("dateColumn")]
    public string DateColumn { get; init; } = "InvoiceDate";

    [JsonPropertyName("companyCodeColumn")]
    public string CompanyCodeColumn { get; init; } = "CompanyCode";

    // Entity → "symmetric" | "upperOnly"
    [JsonPropertyName("dateFilterScope")]
    public Dictionary<string, string>? DateFilterScope { get; init; }
}

public sealed class PagingConfig
{
    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; } = 100;

    [JsonPropertyName("cursorStrategy")]
    public string CursorStrategy { get; init; } = "integer";

    [JsonPropertyName("compositeKey")]
    public string[]? CompositeKey { get; init; }
}

public sealed class FlatViewConfig
{
    [JsonPropertyName("source")]
    public string Source { get; init; } = "";

    [JsonPropertyName("entityColumns")]
    public Dictionary<string, List<string>> EntityColumns { get; init; } = new();
}

public sealed class GroupingConfig
{
    [JsonPropertyName("strategy")]
    public string Strategy { get; init; } = "none";

    [JsonPropertyName("followupDays")]
    public int FollowupDays { get; init; } = 14;

    [JsonPropertyName("groupKeyColumns")]
    public string[]? GroupKeyColumns { get; init; }

    [JsonPropertyName("sortColumn")]
    public string? SortColumn { get; init; }
}

public sealed class PostLoadRuleConfig
{
    [JsonPropertyName("rule")]
    public string Rule { get; init; } = "";

    [JsonPropertyName("params")]
    public System.Text.Json.Nodes.JsonObject? Params { get; init; }
}

public sealed class ValidationGatesConfig
{
    [JsonPropertyName("runOnSessionStart")]
    public bool RunOnSessionStart { get; init; } = true;

    [JsonPropertyName("runBeforeFirstBatch")]
    public bool RunBeforeFirstBatch { get; init; } = true;
}

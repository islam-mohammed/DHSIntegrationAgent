namespace DHSIntegrationAgent.Contracts.DomainMapping;

public sealed record InsertMissMappingDomainRequest(
    string ProviderDhsCode,
    IReadOnlyList<MismappedItem> MismappedItems
);

public sealed record MismappedItem(
    string ProviderCodeValue,
    string ProviderNameValue,
    int DomainTableId,
    string? DomainTableName = null
);

public sealed record InsertMissMappingDomainResult(
    bool Succeeded,
    int StatusCode,
    string? Message,
    IReadOnlyList<string> Errors,
    InsertMissMappingDomainData Data
);

public sealed record InsertMissMappingDomainData(
    int TotalItems,
    int InsertedCount,
    int SkippedCount,
    IReadOnlyList<string> SkippedItems
);

public sealed record GetMissingDomainMappingsResult(
    bool Succeeded,
    int StatusCode,
    string? Message,
    IReadOnlyList<string>? Errors,
    IReadOnlyList<MissingDomainMappingItem>? Data
);

public sealed record MissingDomainMappingItem(
    string? ProviderCodeValue,
    string? ProviderNameValue,
    int DomainTableId,
    string? DomainTableName
);

public sealed record GetProviderDomainMappingsWithMissingResult(
    bool Succeeded,
    int StatusCode,
    string? Message,
    IReadOnlyList<string>? Errors,
    GetProviderDomainMappingsWithMissingData? Data
);

public sealed record GetProviderDomainMappingsWithMissingData(
    IReadOnlyList<DomainMappingDto>? DomainMappings,
    IReadOnlyList<MissingDomainMappingDto>? MissingDomainMappings
);

public sealed record DomainMappingDto(
    int DomTable_ID,
    string? ProviderDomainCode,
    string? ProviderDomainValue,
    int DhsDomainValue,
    bool? IsDefault,
    string? CodeValue,
    string? DisplayValue
);

public sealed record MissingDomainMappingDto(
    string? ProviderCodeValue,
    string? ProviderNameValue,
    int DomainTableId,
    string? DomainTableName
);

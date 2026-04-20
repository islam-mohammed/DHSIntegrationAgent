namespace DHSIntegrationAgent.Sync.Mapper;

public sealed class ColumnManifest
{
    // source column name (upper-cased) → canonical name
    private readonly Dictionary<string, string> _sourceToCanonical;
    private readonly IReadOnlyList<(string source, string canonical)> _columns;
    // canonical name → CLR type declared in descriptor (null when not declared)
    private readonly Dictionary<string, Type?> _canonicalToClrType;

    private ColumnManifest(
        Dictionary<string, string> sourceToCanonical,
        IReadOnlyList<(string source, string canonical)> columns,
        Dictionary<string, Type?> canonicalToClrType)
    {
        _sourceToCanonical = sourceToCanonical;
        _columns           = columns;
        _canonicalToClrType = canonicalToClrType;
    }

    public static ColumnManifest FromDescriptor(IDictionary<string, ColumnFieldDescriptor?> manifestSection)
    {
        var srcToCanon   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cols         = new List<(string, string)>();
        var canonToType  = new Dictionary<string, Type?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (canonical, field) in manifestSection)
        {
            if (field?.Source is null) continue; // null entry or null source = skipColumn
            srcToCanon[field.Source.ToUpperInvariant()] = canonical;
            cols.Add((field.Source, canonical));
            canonToType[canonical] = field.ClrType; // null when type not declared in descriptor
        }

        return new ColumnManifest(srcToCanon, cols, canonToType);
    }

    // Returns null if source column is not in manifest.
    public string? ResolveCanonical(string sourceColumnName)
        => _sourceToCanonical.TryGetValue(sourceColumnName.ToUpperInvariant(), out var canon) ? canon : null;

    // Reverse lookup: canonical name → source column name (case-insensitive).
    public string? GetSourceColumn(string canonicalName)
    {
        foreach (var (src, canon) in _columns)
            if (string.Equals(canon, canonicalName, StringComparison.OrdinalIgnoreCase))
                return src;
        return null;
    }

    // Returns the descriptor-declared CLR type for a canonical name, or null when not declared.
    // Callers should fall back to TypeCoercionMap when this returns null.
    public Type? GetTargetType(string canonicalName)
        => _canonicalToClrType.TryGetValue(canonicalName, out var t) ? t : null;

    // Non-null entries only (for SELECT list construction).
    public IReadOnlyList<(string source, string canonical)> Columns => _columns;
}

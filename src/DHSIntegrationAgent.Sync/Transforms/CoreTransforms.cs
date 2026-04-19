using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Sync.Transforms;

public sealed class UpperTransform : ITransformStep
{
    public string Name => "Upper";

    public void Apply(JsonObject row, string columnName, Dictionary<string, object> parameters)
    {
        var node = row[columnName];
        if (node != null && node.GetValueKind() == System.Text.Json.JsonValueKind.String)
        {
            row[columnName] = node.GetValue<string>().ToUpperInvariant();
        }
    }
}

public sealed class TruncateTransform : ITransformStep
{
    public string Name => "Truncate";

    public void Apply(JsonObject row, string columnName, Dictionary<string, object> parameters)
    {
        var node = row[columnName];
        if (node != null && node.GetValueKind() == System.Text.Json.JsonValueKind.String)
        {
            if (parameters.TryGetValue("length", out var lengthObj) && lengthObj is int length)
            {
                var val = node.GetValue<string>();
                if (val.Length > length)
                {
                    row[columnName] = val.Substring(0, length);
                }
            }
        }
    }
}

public sealed class NullToZeroTransform : ITransformStep
{
    public string Name => "NullToZero";

    public void Apply(JsonObject row, string columnName, Dictionary<string, object> parameters)
    {
        var node = row[columnName];
        if (node == null)
        {
            row[columnName] = 0;
        }
    }
}

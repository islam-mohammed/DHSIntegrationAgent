namespace DHSIntegrationAgent.Sync.Pipeline;

public sealed record ResumeCursor(string Value)
{
    public static ResumeCursor FromInt(int lastSeen) => new(lastSeen.ToString());

    public static ResumeCursor FromComposite(params string[] parts)
        => new(string.Join("|", parts));

    public int AsInt() => int.Parse(Value);

    public string[] AsComposite() => Value.Split('|');
}

namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record PayerItem(
    int PayerId,
    string? PayerName);

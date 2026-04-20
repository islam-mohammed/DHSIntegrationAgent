namespace DHSIntegrationAgent.Sync.Pipeline;

public sealed record ClaimPage(
    IReadOnlyList<int> ClaimKeys,
    ResumeCursor? NextCursor,   // null on the last page
    bool IsLastPage);

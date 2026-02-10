using DHSIntegrationAgent.Contracts.Persistence;
﻿using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class ValidationIssueRepository : SqliteRepositoryBase, IValidationIssueRepository
{
    public ValidationIssueRepository(DbConnection conn, DbTransaction tx) : base(conn, tx) { }

    public async Task InsertAsync(ValidationIssueRow row, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            INSERT INTO ValidationIssue
            (ProviderDhsCode, ProIdClaim, IssueType, FieldPath, RawValue, Message, IsBlocking, CreatedUtc)
            VALUES
            ($p, $id, $t, $fp, $rv, $m, $b, $now);
            """);
        SqliteSqlBuilder.AddParam(cmd, "$p", row.ProviderDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$id", row.ProIdClaim);
        SqliteSqlBuilder.AddParam(cmd, "$t", row.IssueType);
        SqliteSqlBuilder.AddParam(cmd, "$fp", row.FieldPath);
        SqliteSqlBuilder.AddParam(cmd, "$rv", row.RawValue);
        SqliteSqlBuilder.AddParam(cmd, "$m", row.Message);
        SqliteSqlBuilder.AddParam(cmd, "$b", row.IsBlocking ? 1 : 0);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(row.CreatedUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ResolveAsync(long validationIssueId, string resolvedBy, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            UPDATE ValidationIssue
            SET ResolvedUtc = $now,
                ResolvedBy = $by
            WHERE ValidationIssueId = $id;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));
        SqliteSqlBuilder.AddParam(cmd, "$by", resolvedBy);
        SqliteSqlBuilder.AddParam(cmd, "$id", validationIssueId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

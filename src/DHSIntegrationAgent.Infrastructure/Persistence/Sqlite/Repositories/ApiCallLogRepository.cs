using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class ApiCallLogRepository : IApiCallLogRepository
{
    private readonly DbConnection _conn;
    private readonly DbTransaction _tx;

    public ApiCallLogRepository(DbConnection conn, DbTransaction tx)
    {
        _conn = conn;
        _tx = tx;
    }

    public async Task InsertAsync(
        string endpointName,
        string? correlationId,
        DateTimeOffset requestUtc,
        DateTimeOffset? responseUtc,
        int? durationMs,
        int? httpStatusCode,
        bool succeeded,
        string? errorMessage,
        long? requestBytes,
        long? responseBytes,
        bool wasGzipRequest,
        string? providerDhsCode,
        CancellationToken cancellationToken)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText =
            """
            INSERT INTO ApiCallLog
            (ProviderDhsCode, EndpointName, CorrelationId, RequestUtc, ResponseUtc, DurationMs, HttpStatusCode,
             Succeeded, ErrorMessage, RequestBytes, ResponseBytes, WasGzipRequest)
            VALUES
            ($p, $e, $c, $rq, $rs, $d, $h, $s, $err, $qb, $rb, $gz);
            """;

        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$e", endpointName);
        SqliteSqlBuilder.AddParam(cmd, "$c", correlationId);
        SqliteSqlBuilder.AddParam(cmd, "$rq", SqliteUtc.ToIso(requestUtc));
        SqliteSqlBuilder.AddParam(cmd, "$rs", responseUtc is null ? null : SqliteUtc.ToIso(responseUtc.Value));
        SqliteSqlBuilder.AddParam(cmd, "$d", durationMs);
        SqliteSqlBuilder.AddParam(cmd, "$h", httpStatusCode);
        SqliteSqlBuilder.AddParam(cmd, "$s", succeeded ? 1 : 0);
        SqliteSqlBuilder.AddParam(cmd, "$err", errorMessage);
        SqliteSqlBuilder.AddParam(cmd, "$qb", requestBytes);
        SqliteSqlBuilder.AddParam(cmd, "$rb", responseBytes);
        SqliteSqlBuilder.AddParam(cmd, "$gz", wasGzipRequest ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApiCallLogItem>> GetRecentApiCallsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText =
            """
            SELECT 
                ApiCallLogId,
                ProviderDhsCode,
                EndpointName,
                CorrelationId,
                RequestUtc,
                ResponseUtc,
                DurationMs,
                HttpStatusCode,
                Succeeded,
                ErrorMessage,
                RequestBytes,
                ResponseBytes,
                WasGzipRequest
            FROM ApiCallLog
            ORDER BY RequestUtc DESC
            LIMIT $limit;
            """;

        SqliteSqlBuilder.AddParam(cmd, "$limit", limit);

        var list = new List<ApiCallLogItem>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new ApiCallLogItem(
                ApiCallLogId: r.GetInt32(0),
                ProviderDhsCode: r.IsDBNull(1) ? null : r.GetString(1),
                EndpointName: r.GetString(2),
                CorrelationId: r.IsDBNull(3) ? null : r.GetString(3),
                RequestUtc: SqliteUtc.FromIso(r.GetString(4)),
                ResponseUtc: r.IsDBNull(5) ? null : SqliteUtc.FromIso(r.GetString(5)),
                DurationMs: r.IsDBNull(6) ? null : r.GetInt32(6),
                HttpStatusCode: r.IsDBNull(7) ? null : r.GetInt32(7),
                Succeeded: r.GetInt32(8) == 1,
                ErrorMessage: r.IsDBNull(9) ? null : r.GetString(9),
                RequestBytes: r.IsDBNull(10) ? null : r.GetInt64(10),
                ResponseBytes: r.IsDBNull(11) ? null : r.GetInt64(11),
                WasGzipRequest: r.GetInt32(12) == 1
            ));
        }
        return list;
    }
}

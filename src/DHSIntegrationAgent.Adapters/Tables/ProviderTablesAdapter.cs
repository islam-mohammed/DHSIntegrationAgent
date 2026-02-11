using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Providers;
﻿using System.Data;
using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using Microsoft.Data.SqlClient;

namespace DHSIntegrationAgent.Adapters.Tables;

/// <summary>
/// WBS 3.2: Table-to-table provider DB extraction adapter.
///
/// ProviderDhsCode is treated as STRING across the solution.
/// Provider DB schema (tableToTable) stores provider_dhsCode as INT; we compare using TRY_CONVERT(int, @ProviderDhsCode)
/// to keep queries index-friendly where possible.
/// </summary>
public sealed class ProviderTablesAdapter : IProviderTablesAdapter
{
    private readonly IProviderDbFactory _providerDbFactory;
    private readonly ISqliteUnitOfWorkFactory _uowFactory;

    // Defaults derived from the provided tableToTable schema script.
    private const string DefaultHeaderTable = "DHSClaim_Header";
    private const string DefaultDoctorTable = "DHS_Doctor";
    private const string DefaultServiceTable = "DHSService_Details";
    private const string DefaultDiagnosisTable = "DHSDiagnosis_Details";
    private const string DefaultLabTable = "DHSLab_Details";
    private const string DefaultRadiologyTable = "DHSRadiology_Details";
    private const string DefaultAttachmentTable = "DHS_Attachment";
    private const string DefaultOpticalTable = "DHS_OpticalVitalSign";
    private const string DefaultItemDetailsTable = "DHS_ItemDetails";
    private const string DefaultAchiTable = "DHS_ACHI";

    // Date column choice (can be adjusted if you confirm different semantics):
    private const string DefaultHeaderDateColumn = "InvoiceDate";

    // Provider claim key column
    private const string DefaultClaimKeyColumn = "ProIdClaim";

    // Provider DB command timeout
    private const int ProviderDbCommandTimeoutSeconds = 60;

    public ProviderTablesAdapter(
        IProviderDbFactory providerDbFactory,
        ISqliteUnitOfWorkFactory uowFactory)
    {
        _providerDbFactory = providerDbFactory;
        _uowFactory = uowFactory;
    }

    public async Task<int> CountClaimsAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        CancellationToken ct)
    {
        var (headerTable, _, dateCol) = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        using var cmd = handle.Connection.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        cmd.CommandText = $@"
SELECT COUNT(1)
FROM {headerTable}
WHERE CompanyCode = @CompanyCode
  AND provider_dhsCode = TRY_CONVERT(int, @ProviderDhsCode)
  AND {dateCol} >= @StartDate
  AND {dateCol} <= @EndDate
  AND (IsFetched IS NULL OR IsFetched = 0);";

        cmd.Parameters.Add(new SqlParameter("@CompanyCode", SqlDbType.VarChar, 50) { Value = companyCode });
        cmd.Parameters.Add(new SqlParameter("@ProviderDhsCode", SqlDbType.NVarChar, 50) { Value = providerDhsCode });
        cmd.Parameters.Add(new SqlParameter("@StartDate", SqlDbType.DateTime2) { Value = batchStartDateUtc.UtcDateTime });
        cmd.Parameters.Add(new SqlParameter("@EndDate", SqlDbType.DateTime2) { Value = batchEndDateUtc.UtcDateTime });

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<FinancialSummary> GetFinancialSummaryAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        CancellationToken ct)
    {
        var (headerTable, _, dateCol) = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        using var cmd = handle.Connection.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        cmd.CommandText = $@"
SELECT
    COUNT(1) AS TotalClaims,
    SUM(TotalNetAmount) AS TotalNetAmount,
    SUM(ClaimedAmount) AS TotalClaimedAmount,
    SUM(TotalDiscount) AS TotalDiscount,
    SUM(TotalDeductible) AS TotalDeductible
FROM {headerTable}
WHERE CompanyCode = @CompanyCode
  AND provider_dhsCode = TRY_CONVERT(int, @ProviderDhsCode)
  AND {dateCol} >= @StartDate
  AND {dateCol} <= @EndDate
  AND (IsFetched IS NULL OR IsFetched = 0);";

        cmd.Parameters.Add(new SqlParameter("@CompanyCode", SqlDbType.VarChar, 50) { Value = companyCode });
        cmd.Parameters.Add(new SqlParameter("@ProviderDhsCode", SqlDbType.NVarChar, 50) { Value = providerDhsCode });
        cmd.Parameters.Add(new SqlParameter("@StartDate", SqlDbType.DateTime2) { Value = batchStartDateUtc.UtcDateTime });
        cmd.Parameters.Add(new SqlParameter("@EndDate", SqlDbType.DateTime2) { Value = batchEndDateUtc.UtcDateTime });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new FinancialSummary(
                TotalClaims: reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                TotalNetAmount: reader.IsDBNull(1) ? 0m : Convert.ToDecimal(reader.GetValue(1)),
                TotalClaimedAmount: reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader.GetValue(2)),
                TotalDiscount: reader.IsDBNull(3) ? 0m : Convert.ToDecimal(reader.GetValue(3)),
                TotalDeductible: reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetValue(4))
            );
        }

        return new FinancialSummary(0, 0, 0, 0, 0);
    }

    public async Task<IReadOnlyList<int>> ListClaimKeysAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        int pageSize,
        int? lastSeenClaimKey,
        CancellationToken ct)
    {
        if (pageSize <= 0) pageSize = 500;
        if (pageSize > 5000) pageSize = 5000;

        var (headerTable, claimKeyCol, dateCol) = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        using var cmd = handle.Connection.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        cmd.CommandText = $@"
SELECT TOP (@PageSize) {claimKeyCol}
FROM {headerTable}
WHERE CompanyCode = @CompanyCode
  AND provider_dhsCode = TRY_CONVERT(int, @ProviderDhsCode)
  AND {dateCol} >= @StartDate
  AND {dateCol} <= @EndDate
  AND (IsFetched IS NULL OR IsFetched = 0)
  AND (@LastSeen IS NULL OR {claimKeyCol} > @LastSeen)
ORDER BY {claimKeyCol} ASC;";

        cmd.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });
        cmd.Parameters.Add(new SqlParameter("@CompanyCode", SqlDbType.VarChar, 50) { Value = companyCode });
        cmd.Parameters.Add(new SqlParameter("@ProviderDhsCode", SqlDbType.NVarChar, 50) { Value = providerDhsCode });
        cmd.Parameters.Add(new SqlParameter("@StartDate", SqlDbType.DateTime2) { Value = batchStartDateUtc.UtcDateTime });
        cmd.Parameters.Add(new SqlParameter("@EndDate", SqlDbType.DateTime2) { Value = batchEndDateUtc.UtcDateTime });
        cmd.Parameters.Add(new SqlParameter("@LastSeen", SqlDbType.Int) { Value = (object?)lastSeenClaimKey ?? DBNull.Value });

        var keys = new List<int>(capacity: Math.Min(pageSize, 1024));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            keys.Add(reader.GetInt32(0));
        }

        return keys;
    }

    public async Task<ProviderClaimBundleRaw?> GetClaimBundleRawAsync(
        string providerDhsCode,
        int proIdClaim,
        CancellationToken ct)
    {
        var (headerTable, claimKeyCol, _) = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        // Header
        var header = await ReadSingleRowAsync(handle.Connection, $@"
SELECT *
FROM {headerTable}
WHERE {claimKeyCol} = @ClaimKey
  AND provider_dhsCode = TRY_CONVERT(int, @ProviderDhsCode);",
            providerDhsCode,
            proIdClaim,
            ct,
            includeProviderFilter: true);

        if (header is null)
            return null;

        // Doctor (by ProIdClaim; no provider filter in schema)
        var doctor = await ReadSingleRowAsync(handle.Connection, $@"
SELECT TOP (1) *
FROM {DefaultDoctorTable}
WHERE ProIdClaim = @ClaimKey;",
            providerDhsCode,
            proIdClaim,
            ct,
            includeProviderFilter: false);

        // Details arrays (most keyed by ProIdClaim; no provider filter columns in the script)
        var services = await ReadManyRowsAsync(handle.Connection, $@"
SELECT *
FROM {DefaultServiceTable}
WHERE ProIdClaim = @ClaimKey;", providerDhsCode, proIdClaim, ct, includeProviderFilter: false);

        var diagnoses = await ReadManyRowsAsync(handle.Connection, $@"
SELECT *
FROM {DefaultDiagnosisTable}
WHERE ProIdClaim = @ClaimKey;", providerDhsCode, proIdClaim, ct, includeProviderFilter: false);

        var labs = await ReadManyRowsAsync(handle.Connection, $@"
SELECT *
FROM {DefaultLabTable}
WHERE ProIdClaim = @ClaimKey;", providerDhsCode, proIdClaim, ct, includeProviderFilter: false);

        var radiology = await ReadManyRowsAsync(handle.Connection, $@"
SELECT *
FROM {DefaultRadiologyTable}
WHERE ProIdClaim = @ClaimKey;", providerDhsCode, proIdClaim, ct, includeProviderFilter: false);

        var attachments = await ReadManyRowsAsync(handle.Connection, $@"
SELECT *
FROM {DefaultAttachmentTable}
WHERE ProIdClaim = @ClaimKey;", providerDhsCode, proIdClaim, ct, includeProviderFilter: false);

        var optical = await ReadManyRowsAsync(handle.Connection, $@"
SELECT *
FROM {DefaultOpticalTable}
WHERE ProIdClaim = @ClaimKey;", providerDhsCode, proIdClaim, ct, includeProviderFilter: false);

        var itemDetails = await ReadManyRowsAsync(handle.Connection, $@"
SELECT *
FROM {DefaultItemDetailsTable}
WHERE ProIdClaim = @ClaimKey;", providerDhsCode, proIdClaim, ct, includeProviderFilter: false);

        var achi = await ReadManyRowsAsync(handle.Connection, $@"
SELECT *
FROM {DefaultAchiTable}
WHERE ProIdClaim = @ClaimKey;", providerDhsCode, proIdClaim, ct, includeProviderFilter: false);

        return new ProviderClaimBundleRaw(
            ProIdClaim: proIdClaim,
            Header: header,
            Doctor: doctor,
            Services: services,
            Diagnoses: diagnoses,
            Labs: labs,
            Radiology: radiology,
            Attachments: attachments,
            OpticalVitalSigns: optical,
            ItemDetails: itemDetails,
            Achi: achi);
    }

    private async Task<(string HeaderTable, string ClaimKeyCol, string DateCol)> ResolveConfigAsync(string providerDhsCode, CancellationToken ct)
    {
        await using var uow = await _uowFactory.CreateAsync(ct);

        var profile = await uow.ProviderProfiles.GetActiveByProviderDhsCodeAsync(providerDhsCode, ct);
        if (profile is null)
            return (DefaultHeaderTable, DefaultClaimKeyColumn, DefaultHeaderDateColumn);

        var extraction = await uow.ProviderExtractionConfigs.GetAsync(new ProviderKey(profile.ProviderCode), ct);

        var claimKey = string.IsNullOrWhiteSpace(extraction?.ClaimKeyColumnName)
            ? DefaultClaimKeyColumn
            : extraction!.ClaimKeyColumnName!;

        var headerTable = string.IsNullOrWhiteSpace(extraction?.HeaderSourceName)
            ? DefaultHeaderTable
            : extraction!.HeaderSourceName!;

        return (headerTable, claimKey, DefaultHeaderDateColumn);
    }

    private static async Task<System.Text.Json.Nodes.JsonObject?> ReadSingleRowAsync(
        DbConnection conn,
        string sql,
        string providerDhsCode,
        int claimKey,
        CancellationToken ct,
        bool includeProviderFilter)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;
        cmd.CommandText = sql;

        cmd.Parameters.Add(new SqlParameter("@ClaimKey", SqlDbType.Int) { Value = claimKey });

        if (includeProviderFilter)
        {
            cmd.Parameters.Add(new SqlParameter("@ProviderDhsCode", SqlDbType.NVarChar, 50) { Value = providerDhsCode });
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return reader.ReadRowAsJsonObject();
    }

    private static async Task<System.Text.Json.Nodes.JsonArray> ReadManyRowsAsync(
        DbConnection conn,
        string sql,
        string providerDhsCode,
        int claimKey,
        CancellationToken ct,
        bool includeProviderFilter)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;
        cmd.CommandText = sql;

        cmd.Parameters.Add(new SqlParameter("@ClaimKey", SqlDbType.Int) { Value = claimKey });

        if (includeProviderFilter)
        {
            cmd.Parameters.Add(new SqlParameter("@ProviderDhsCode", SqlDbType.NVarChar, 50) { Value = providerDhsCode });
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAllRowsAsJsonArrayAsync(ct);
    }
}

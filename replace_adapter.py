import os

filepath = 'src/DHSIntegrationAgent.Adapters/Tables/ProviderTablesAdapter.cs'
with open(filepath, 'r') as f:
    content = f.read()

# Make sure we didn't already replace it
if 'var (headerTable, claimKeyCol, dateCol)' in content:
    content = content.replace('var (headerTable, _, dateCol) = await ResolveConfigAsync(providerDhsCode, ct);', 'var config = await ResolveConfigAsync(providerDhsCode, ct);')
    content = content.replace('var (headerTable, claimKeyCol, dateCol) = await ResolveConfigAsync(providerDhsCode, ct);', 'var config = await ResolveConfigAsync(providerDhsCode, ct);')
    content = content.replace('var (headerTable, claimKeyCol, _) = await ResolveConfigAsync(providerDhsCode, ct);', 'var config = await ResolveConfigAsync(providerDhsCode, ct);')

    # In CountClaimsAsync
    content = content.replace('FROM {headerTable}', 'FROM {config.HeaderSourceName}')
    content = content.replace('AND {dateCol}', 'AND {config.DateColumnName}')

    # In CountAttachmentsAsync
    content = content.replace('FROM {DefaultAttachmentTable} a', 'FROM {config.AttachmentSourceName} a')
    content = content.replace('INNER JOIN {headerTable} h ON a.{DefaultClaimKeyColumn} = h.{claimKeyCol}', 'INNER JOIN {config.HeaderSourceName} h ON a.{config.ClaimKeyColumnName} = h.{config.ClaimKeyColumnName}')

    # In ListClaimKeysAsync
    content = content.replace('SELECT TOP (@PageSize) {claimKeyCol}', 'SELECT TOP (@PageSize) {config.ClaimKeyColumnName}')
    content = content.replace('AND (@LastSeen IS NULL OR {claimKeyCol} > @LastSeen)', 'AND (@LastSeen IS NULL OR {config.ClaimKeyColumnName} > @LastSeen)')
    content = content.replace('ORDER BY {claimKeyCol} ASC', 'ORDER BY {config.ClaimKeyColumnName} ASC')

    # In GetAttachmentsForBatchAsync
    content = content.replace('FROM {DefaultAttachmentTable} a', 'FROM {config.AttachmentSourceName} a')

    # In GetClaimBundlesRawBatchAsync
    content = content.replace('await FetchHeaderBatchAsync(handle.Connection, headerTable, claimKeyCol, providerDhsCode, proIdClaims, ct);', 'await FetchHeaderBatchAsync(handle.Connection, config.HeaderSourceName, config.ClaimKeyColumnName, providerDhsCode, proIdClaims, ct);')
    content = content.replace('await FetchSingleRowBatchAsync(handle.Connection, DefaultDoctorTable, "ProIdClaim", proIdClaims, ct);', 'await FetchSingleRowBatchAsync(handle.Connection, config.DoctorSourceName, config.ClaimKeyColumnName, proIdClaims, ct);')
    content = content.replace('await FetchManyRowsBatchAsync(handle.Connection, DefaultServiceTable, "ProIdClaim", proIdClaims, ct);', 'await FetchManyRowsBatchAsync(handle.Connection, config.ServiceSourceName, config.ClaimKeyColumnName, proIdClaims, ct);')
    content = content.replace('await FetchManyRowsBatchAsync(handle.Connection, DefaultDiagnosisTable, "ProIdClaim", proIdClaims, ct);', 'await FetchManyRowsBatchAsync(handle.Connection, config.DiagnosisSourceName, config.ClaimKeyColumnName, proIdClaims, ct);')
    content = content.replace('await FetchManyRowsBatchAsync(handle.Connection, DefaultLabTable, "ProIdClaim", proIdClaims, ct);', 'await FetchManyRowsBatchAsync(handle.Connection, config.LabSourceName, config.ClaimKeyColumnName, proIdClaims, ct);')
    content = content.replace('await FetchManyRowsBatchAsync(handle.Connection, DefaultRadiologyTable, "ProIdClaim", proIdClaims, ct);', 'await FetchManyRowsBatchAsync(handle.Connection, config.RadiologySourceName, config.ClaimKeyColumnName, proIdClaims, ct);')
    content = content.replace('await FetchManyRowsBatchAsync(handle.Connection, DefaultAttachmentTable, "ProIdClaim", proIdClaims, ct);', 'await FetchManyRowsBatchAsync(handle.Connection, config.AttachmentSourceName, config.ClaimKeyColumnName, proIdClaims, ct);')
    content = content.replace('await FetchManyRowsBatchAsync(handle.Connection, DefaultOpticalTable, "ProIdClaim", proIdClaims, ct);', 'await FetchManyRowsBatchAsync(handle.Connection, config.OpticalSourceName, config.ClaimKeyColumnName, proIdClaims, ct);')
    content = content.replace('await FetchManyRowsBatchAsync(handle.Connection, DefaultItemDetailsTable, "ProIdClaim", proIdClaims, ct);', 'await FetchManyRowsBatchAsync(handle.Connection, config.ItemDetailsSourceName, config.ClaimKeyColumnName, proIdClaims, ct);')
    content = content.replace('await FetchManyRowsBatchAsync(handle.Connection, DefaultAchiTable, "ProIdClaim", proIdClaims, ct);', 'await FetchManyRowsBatchAsync(handle.Connection, config.AchiSourceName, config.ClaimKeyColumnName, proIdClaims, ct);')

    with open(filepath, 'w') as f:
        f.write(content)

    print("Replaced!")
else:
    print("Already applied!")

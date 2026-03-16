1. **Update `ProviderProfileRow` contract**
   - Add `byte[]? EncryptedBlobStorageConnectionString` and `string? BlobStorageContainerName` to `src/DHSIntegrationAgent.Contracts/Persistence/ProviderProfileRow.cs`.
2. **Update SQLite schema and migrations**
   - Update `src/DHSIntegrationAgent.Infrastructure/Persistence/Sqlite/SqliteMigrations.cs` to add `EncryptedBlobStorageConnectionString BLOB NULL` and `BlobStorageContainerName TEXT NULL` to `ProviderProfile` table in migration 5.3. Also create an `ALTER TABLE` in `_migrations` list if applicable. (Wait, let's just do ALTER TABLE for existing DBs and update the CREATE TABLE block).
3. **Update `ProviderProfileRepository`**
   - Update `UpsertAsync`, `GetAsync`, `GetActiveByProviderDhsCodeAsync`, and `ReadRow` in `src/DHSIntegrationAgent.Infrastructure/Persistence/Sqlite/Repositories/ProviderProfileRepository.cs` to handle the new columns.
4. **Update `ProviderConfigurationService`**
   - In `TryUpsertProviderProfileFromConfigAsync` (src/DHSIntegrationAgent.Infrastructure/Providers/ProviderConfigurationService.cs), extract `blobStorageConnectionString` and `blobStorageContainerName` from `providerInfo` (or maybe root `payload` depending on where it lives in the API response - I need to double check swagger or just try parsing from `providerInfo` if it's there. Actually, let's assume it's in `providerInfo`).
   - Encrypt `blobStorageConnectionString` and save it to the `ProviderProfileRow` along with `blobStorageContainerName`.
   - Ensure these sensitive properties are also removed in `ScrubSecrets`.
5. **Update `AttachmentService`**
   - In `src/DHSIntegrationAgent.Infrastructure/Services/AttachmentService.cs`, `UploadAsync`, instead of just checking `_options.AttachmentBlobStorageCon`, fetch the `ProviderProfile` for the provider (it has `attachment.ProviderDhsCode`), decrypt `EncryptedBlobStorageConnectionString`, and use it if available. If not, fallback to options or SAS URL.
6. **Pre-commit checks**
   - Run tests and build.

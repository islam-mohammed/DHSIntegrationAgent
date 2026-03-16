1. **Update `ProviderProfileRow` contract**
   - Add `byte[]? EncryptedBlobStorageConnectionString` and `string? BlobStorageContainerName` to `src/DHSIntegrationAgent.Contracts/Persistence/ProviderProfileRow.cs`.
2. **Update SQLite schema and migrations**
   - Update `src/DHSIntegrationAgent.Infrastructure/Persistence/Sqlite/SqliteMigrations.cs` to add `EncryptedBlobStorageConnectionString BLOB NULL` and `BlobStorageContainerName TEXT NULL` to the `ProviderProfile` table creation.
   - Add `ALTER TABLE` statements in `_migrations` list to add these columns to existing databases.
3. **Update `ProviderProfileRepository`**
   - Update `UpsertAsync`, `GetAsync`, `GetActiveByProviderDhsCodeAsync`, and `ReadRow` in `src/DHSIntegrationAgent.Infrastructure/Persistence/Sqlite/Repositories/ProviderProfileRepository.cs` to include the new columns.
4. **Update `ProviderConfigurationService`**
   - Extract `blobStorageConnectionString` and `containerName` (or `blobStorageContainerName`, whatever is returned) from the `providerInfo` JSON object in `TryUpsertProviderProfileFromConfigAsync` (src/DHSIntegrationAgent.Infrastructure/Providers/ProviderConfigurationService.cs).
   - If `blobStorageConnectionString` is provided, encrypt it and store it in `EncryptedBlobStorageConnectionString`. Store `containerName` in `BlobStorageContainerName`.
   - Update `ScrubSecrets` to remove `blobStorageConnectionString`.
5. **Update `AttachmentService`**
   - In `src/DHSIntegrationAgent.Infrastructure/Services/AttachmentService.cs`, `UploadAsync`, get the provider profile for the current provider (it has `attachment.ProviderDhsCode`) via `_uowFactory` -> `ProviderProfiles.GetActiveByProviderDhsCodeAsync`.
   - If `profile.EncryptedBlobStorageConnectionString` is present, decrypt it and use it, along with `profile.BlobStorageContainerName`.
   - If not present in the profile, fall back to `_options.AttachmentBlobStorageCon` and `_options.AttachmentBlobStorageContainer`. If none available, fall back to SAS URL.
6. **Pre-commit checks**
   - Complete pre commit steps to make sure proper testing, verifications, reviews and reflections are done.
7. **Submit the change.**

#!/bin/bash
sed -i '/"CREATE INDEX IX_Batch_BcrId ON Batch(BcrId);",/a \
        // -----------------------\n        // V2 Migration\n        // -----------------------\n        "ALTER TABLE Batch ADD COLUMN CreatedByUserName TEXT NULL;",' src/DHSIntegrationAgent.Infrastructure/Persistence/Sqlite/SqliteMigrations.cs

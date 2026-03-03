import os

filepath = 'src/DHSIntegrationAgent.Infrastructure/Persistence/Sqlite/SqliteMigrations.cs'
with open(filepath, 'r') as f:
    content = f.read()

content = content.replace(
    '"ALTER TABLE ProviderExtractionConfig ADD COLUMN DateColumnName TEXT NULL;"',
    '"ALTER TABLE ProviderExtractionConfig ADD COLUMN DateColumnName TEXT NULL;",\n        "ALTER TABLE ProviderExtractionConfig ADD COLUMN CustomItemSql TEXT NULL;",\n        "ALTER TABLE ProviderExtractionConfig ADD COLUMN CustomAchiSql TEXT NULL;"'
)

# And in BuildV1:
content = content.replace(
    '''            CustomOpticalSql       TEXT NULL,
            Notes                  TEXT NULL,''',
    '''            CustomOpticalSql       TEXT NULL,
            CustomItemSql          TEXT NULL,
            CustomAchiSql          TEXT NULL,
            Notes                  TEXT NULL,'''
)

with open(filepath, 'w') as f:
    f.write(content)

print("Patched!")

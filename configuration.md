# DHS Integration Agent — Configuration

## Overview
Configuration comes from:
1) `appsettings.json` / `appsettings.Development.json` (non-secret)
2) Environment variables prefixed with `DHSAGENT_` (non-secret)
3) SQLite `AppSettings` / secure settings (secrets, DPAPI-protected)

## Non-secret configuration (OK in JSON/env vars)
### App
- `App:EnvironmentName` (e.g., Development)
- `App:DatabasePath` (optional; if empty, defaults to LocalAppData\DHSIntegrationAgent\agent.db)
- `App:LogFolder` (optional)

### Worker
- `Worker:*` polling/lease intervals

### Api
- `Api:BaseUrl` (REQUIRED; absolute http/https)
- `Api:TimeoutSeconds`

## Secrets policy (WBS 0.3 / PHI spec)
Do NOT store secrets in:
- `appsettings.json`
- environment variables
- logs

Examples of secrets:
- Provider DB connection strings/passwords
- Azure Blob SAS URLs
- tokens/refresh tokens (if ever stored)

Secrets must be:
- Stored encrypted at rest using DPAPI (Windows CurrentUser scope)
- Persisted only as protected blobs in SQLite
- Decrypted only in memory, only when needed

## Environment variable overrides
Use double underscore for nesting:
- `DHSAGENT_Api__BaseUrl=https://our-backend.example/`
- `DHSAGENT_Api__TimeoutSeconds=60`

## Local run checklist
1) Set `Api:BaseUrl` in appsettings or env var.
2) Run the WPF app.
3) On first run, the app initializes SQLite and local settings tables (if applicable).

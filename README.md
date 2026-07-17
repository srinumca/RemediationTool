# GFR Remediation Tool

## Current application scope

This phase supports only the two-step inbound-file workflow:

1. Upload an EDG CSV or XLSX file.
2. Process the uploaded file by its generated ReportUID.

The application continues to:

- store source files in S3 or local storage;
- create and update ingestion job records;
- parse and validate inbound records;
- persist valid findings and rejected rows;
- create and verify the Parquet working file;
- process findings in batches with bounded concurrency and retry;
- write checkpoint progress and processing summaries;
- record application and audit logs.

Dashboard, status, direct upload-and-ingest, and resume APIs are intentionally outside the current phase.

## Retained APIs

### Upload

| Method | Endpoint | Purpose |
|---|---|---|
| `POST` | `/api/upload` | Stores the source file and creates an ingestion job. Returns `202 Accepted` with the generated ReportUID. |

### Ingestion

| Method | Endpoint | Purpose |
|---|---|---|
| `POST` | `/api/ingestion/{reportUid}` | Downloads and processes the previously uploaded file identified by ReportUID. |

## Processing flow

```text
POST /api/upload
        ↓
Store source file and metadata
        ↓
Create ingestion job and return ReportUID
        ↓
POST /api/ingestion/{reportUid}
        ↓
Download, parse and validate
        ↓
Persist rejected rows and valid findings
        ↓
Write Parquet, checkpoints, summary and audit logs
```

## Retained supporting capabilities

- Microsoft Entra ID JWT validation.
- Swagger OAuth 2.0 Authorization Code flow with PKCE.
- User-role and application-role authorization.
- Serilog application, audit, and HTTP request logging.
- Global exception handling.
- FluentValidation.
- S3 and local storage implementations.
- DynamoDB and JSON persistence implementations.
- CSV and XLSX parsing.
- Parquet working-file creation and verification.
- Batch persistence with bounded concurrency and retry.
- Checkpoint progress writes.
- Temporary staging write and cleanup.
- Rejected-row persistence.

## Authorization matrix

| Endpoint | Required caller when authentication is enabled |
|---|---|
| `POST /api/upload` | User token with `access_as_user` and `Admin` or `System_Admin` role |
| `POST /api/ingestion/{reportUid}` | Application token with `access_as_application` role |

Authentication is disabled by default until the real Microsoft Entra registration values are supplied.

## Microsoft Entra configuration

Create two app registrations:

1. **GFR Remediation Tool API**
   - Single-tenant API.
   - Delegated scope: `access_as_user`.
   - Application role: `access_as_application`.
   - User roles required by the retained upload API: `System_Admin` and `Admin`.

2. **GFR Remediation Tool Swagger**
   - Public browser client.
   - Authorization Code flow with PKCE.
   - Permission to request the API's `access_as_user` scope.
   - No client secret in Swagger.

Local Swagger redirect URI:

```text
https://localhost:58207/swagger/oauth2-redirect.html
```

Environment variables:

```text
Authentication__Enabled=true
Swagger__Enabled=true
AzureAd__TenantId=<tenant-id>
AzureAd__ClientId=<api-client-id>
AzureAd__Audience=api://<api-client-id>
AzureAd__Scopes=access_as_user
AzureAd__ApplicationRole=access_as_application
SwaggerAzureAd__ClientId=<swagger-client-id>
SwaggerAzureAd__Scope=api://<api-client-id>/access_as_user
Authorization__Roles__SystemAdmin=System_Admin
Authorization__Roles__Admin=Admin
```

The caller of the ingestion endpoint must obtain an Entra app-only token containing the `access_as_application` role before authentication is enabled.

## Validation

Run before merging or deploying:

```text
dotnet restore
dotnet build RemediationTool.sln
dotnet test RemediationTool.sln
```

Then verify:

- `POST /api/upload` returns `202 Accepted`, stores the file and creates the job record.
- `POST /api/ingestion/{reportUid}` processes the previously uploaded file.
- Valid findings and rejected rows are persisted.
- Parquet writing and verification remain operational.
- Batch retry, checkpoint writes, summaries and audit logs remain operational.
- Missing tokens return `401` when authentication is enabled.
- Invalid roles or scopes return `403`.
- Valid user and application tokens access only their intended endpoints.

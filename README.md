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
- Optional Swagger OAuth 2.0 Authorization Code flow with PKCE.
- Authenticated-token and admin-role authorization.
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
| `POST /api/upload` | Authenticated token with `Admin` or `System_Admin` role |
| `POST /api/ingestion/{reportUid}` | Any authenticated token issued for this API |

Authentication is disabled by default until the real Microsoft Entra registration values are supplied.

## Microsoft Entra configuration

Create the API app registration as a single-tenant API. The retained upload API expects the `System_Admin` or `Admin` role when role-based upload authorization is required.

A separate Swagger browser client is optional. When configured, it uses Authorization Code flow with PKCE and requests the API scope supplied through `SwaggerAzureAd:Scope`. No client secret is stored in Swagger.

Local Swagger redirect URI:

```text
https://localhost:58207/swagger/oauth2-redirect.html
```

Required API environment variables:

```text
Authentication__Enabled=true
AzureAd__TenantId=<tenant-id>
AzureAd__ClientId=<api-client-id>
AzureAd__Audience=api://<api-client-id>
Authorization__Roles__SystemAdmin=System_Admin
Authorization__Roles__Admin=Admin
```

Optional Swagger OAuth environment variables:

```text
Swagger__Enabled=true
SwaggerAzureAd__ClientId=<swagger-client-id>
SwaggerAzureAd__Scope=<configured-api-scope>
```

The API can validate and authorize bearer tokens without `AzureAd:Scopes` or `AzureAd:ApplicationRole`. Swagger OAuth is enabled only when both optional Swagger settings are present.

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
- Missing or invalid tokens return `401` when authentication is enabled.
- Authenticated callers without the required upload role receive `403` for `POST /api/upload`.
- Any valid authenticated token issued for the API can call `POST /api/ingestion/{reportUid}`.

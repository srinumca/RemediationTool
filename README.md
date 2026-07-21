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
- Swagger JWT bearer-token authorization.
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

Authentication is enabled in the supplied application settings using the configured Microsoft Entra tenant ID, API client ID, and audience.

## Microsoft Entra configuration

Create the API app registration as a single-tenant API. The retained upload API expects the `System_Admin` or `Admin` role when role-based upload authorization is required.

Required API environment variables:

```text
Authentication__Enabled=true
AzureAd__TenantId=<tenant-id>
AzureAd__ClientId=<api-client-id>
AzureAd__Audience=api://<api-client-id>
Authorization__Roles__SystemAdmin=System_Admin
Authorization__Roles__Admin=Admin
```

The API validates bearer tokens without `AzureAd:Scopes` or `AzureAd:ApplicationRole` configuration.

## Swagger authorization

When authentication is enabled, Swagger displays the **Authorize** button using the HTTP bearer security scheme.

1. Obtain a Microsoft Entra access token issued for this API.
2. Open Swagger and select **Authorize**.
3. Paste the JWT access token into the value field. Swagger adds the `Bearer` prefix automatically.
4. Select **Authorize**, close the dialog, and invoke the API.

No separate Swagger client ID, client secret, or Swagger scope configuration is required for this manual bearer-token flow.

## Validation

Run before merging or deploying:

```text
dotnet restore
dotnet build RemediationTool.sln
dotnet test RemediationTool.sln --settings coverage.runsettings --collect:"XPlat Code Coverage"
```

Then verify:

- Swagger displays the **Authorize** button.
- A valid API access token can be entered through Swagger.
- `POST /api/upload` returns `202 Accepted` for a token containing `Admin` or `System_Admin`.
- `POST /api/ingestion/{reportUid}` processes the previously uploaded file for a valid API token.
- Valid findings and rejected rows are persisted.
- Parquet writing and verification remain operational.
- Batch retry, checkpoint writes, summaries and audit logs remain operational.
- Missing or invalid tokens return `401` when authentication is enabled.
- Authenticated callers without the required upload role receive `403` for `POST /api/upload`.

# GFR Remediation Tool

## Current application scope

This phase of the application supports only the inbound-file workflow and its operational dashboard:

- Upload an EDG CSV or XLSX file.
- Store the source file in the configured storage provider.
- Create and track the ingestion job.
- Parse and validate inbound records.
- Persist valid findings and rejected rows.
- Create and validate the Parquet working file.
- Process findings in batches with retry and checkpoint support.
- Resume failed or partially completed ingestion jobs.
- Display jobs, findings, and rejected rows through the dashboard APIs and static dashboard page.

Quarantine, restore, deletion, retention deletion, and separate report APIs are intentionally outside the current phase.

## Retained APIs

### Upload

| Method | Endpoint | Purpose |
|---|---|---|
| `POST` | `/api/upload` | Uploads the source file and creates an ingestion job. |
| `GET` | `/api/upload/{reportUid}` | Returns the upload/job status. |

### Ingestion

| Method | Endpoint | Purpose |
|---|---|---|
| `POST` | `/api/ingestion/{reportUid}` | Processes an uploaded file. |
| `POST` | `/api/ingestion/{reportUid}/resume` | Resumes from the latest checkpoint. |
| `GET` | `/api/ingestion/{reportUid}/status` | Returns ingestion status. |

### Dashboard

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `/api/dashboard/jobs` | Returns ingestion jobs. |
| `GET` | `/api/dashboard/jobs/{jobId}` | Returns one ingestion job. |
| `GET` | `/api/dashboard/jobs/{jobId}/findings` | Returns successful findings for a job. |
| `GET` | `/api/dashboard/jobs/{jobId}/rejected` | Returns rejected rows for a job. |
| `GET` | `/api/dashboard/rejected` | Returns rejected rows across jobs. |

## Retained supporting capabilities

- Microsoft Entra ID JWT validation.
- Swagger OAuth 2.0 Authorization Code flow with PKCE.
- User-role and application-role authorization.
- Serilog application, audit, and HTTP request logging.
- Global exception handling.
- FluentValidation.
- S3 and local storage implementations.
- DynamoDB and JSON persistence implementations.
- CSV/XLSX parsing.
- Parquet working-file processing.
- Batch persistence with bounded concurrency and retry.
- Checkpoint and resume support.
- Static dashboard UI.

## Authorization matrix

| Endpoint area | Required caller when authentication is enabled |
|---|---|
| Upload and upload status | User token with `access_as_user` and `Admin` or `System_Admin` role |
| Ingestion, resume, and ingestion status | App token with `access_as_application` role |
| Dashboard | User token with `access_as_user` and `System_Admin`, `Admin`, `User`, or `View_Only` role |

Authentication is disabled by default until the real Microsoft Entra registration values are supplied.

## Microsoft Entra configuration

Create two app registrations:

1. **GFR Remediation Tool API**
   - Single-tenant API.
   - Delegated scope: `access_as_user`.
   - Application role: `access_as_application`.
   - User roles: `System_Admin`, `Admin`, `User`, and `View_Only`.

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
Authorization__Roles__User=User
Authorization__Roles__ViewOnly=View_Only
```

AWS Step Functions or another approved machine caller must obtain an Entra app-only access token containing `access_as_application` before authentication is enabled.

## Validation

Run before merging or deploying:

```text
dotnet restore
dotnet build RemediationTool.sln
dotnet test RemediationTool.sln
```

Then verify:

- Upload returns `202 Accepted` and creates a job.
- Ingestion processes valid and rejected rows.
- Parquet, retry, checkpoint, and resume paths remain operational.
- Dashboard APIs return jobs, findings, and rejected rows.
- Missing token returns `401` when authentication is enabled.
- Invalid role/scope returns `403`.
- Valid user and application tokens access only their intended endpoints.

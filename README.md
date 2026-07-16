# RemediationTool_POC

## Microsoft Entra ID authentication

The API supports Microsoft Entra ID JWT access-token validation and Swagger OAuth 2.0 Authorization Code flow with PKCE.

Authentication remains disabled by default so the application can continue to run until the Entra registrations and environment values are available. Controller authorization policies are already implemented and become active when `Authentication:Enabled` is set to `true`.

## Required Entra registrations

Create two app registrations.

### 1. GFR Remediation Tool API

- Single-tenant web API.
- Expose delegated scope `access_as_user` for Swagger and UI calls.
- Expose application role `access_as_application` for machine-to-machine callers.
- Expose these user app-role values:
  - `System_Admin`
  - `Admin`
  - `User`
  - `View_Only`
- The complete delegated scope normally looks like `api://<api-client-id>/access_as_user`.

### 2. GFR Remediation Tool Swagger

- Public/browser client.
- Add the API's delegated `access_as_user` permission.
- Configure Authorization Code flow with PKCE.
- Do not configure or expose a client secret in Swagger.

For local development, register:

```text
https://localhost:58207/swagger/oauth2-redirect.html
```

Add the equivalent redirect URI for DEV, QA, staging, and any other environment where Swagger is approved.

## Configuration

Provide real values through environment-specific configuration or deployment variables:

```json
{
  "Authentication": {
    "Enabled": true
  },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<tenant-id>",
    "ClientId": "<api-application-client-id>",
    "Audience": "api://<api-application-client-id>",
    "Scopes": "access_as_user",
    "ApplicationRole": "access_as_application"
  },
  "SwaggerAzureAd": {
    "ClientId": "<swagger-application-client-id>",
    "Scope": "api://<api-application-client-id>/access_as_user"
  },
  "Authorization": {
    "Roles": {
      "SystemAdmin": "System_Admin",
      "Admin": "Admin",
      "User": "User",
      "ViewOnly": "View_Only"
    }
  }
}
```

ASP.NET Core environment-variable names use double underscores:

```text
Authentication__Enabled=true
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

## Authorization matrix

| Endpoint area | Required caller |
|---|---|
| Upload and upload status | `Admin` or `System_Admin` user |
| Queue quarantine | `Admin` or `System_Admin` user |
| Execute quarantine processing | App token with `access_as_application` |
| Restore one or all files | `Admin` or `System_Admin` user |
| Manual delete one or all files | `System_Admin` user |
| Automated retention deletion | App token with `access_as_application` |
| Ingestion, resume, ingestion status | App token with `access_as_application` |
| Reports and dashboard | `System_Admin`, `Admin`, `User`, or `View_Only` user |

A user call must contain both the delegated `access_as_user` scope and an allowed business app role. An internal service call must contain the `access_as_application` app role.

## Swagger login flow

1. Start the API in the Development environment.
2. Open `/swagger`.
3. Select **Authorize**.
4. Sign in with a Microsoft Entra account.
5. Approve the API permission when required.
6. Swagger sends `Authorization: Bearer <token>` with requests.

## Non-user callers

AWS Step Functions and other machine callers must obtain an Entra app-only token through the client-credentials flow, or through an approved workload-identity/federation setup. The service principal must be assigned the API's `access_as_application` role. Without that token, internal ingestion, quarantine-run, and retention-delete calls receive `401 Unauthorized` or `403 Forbidden`.

## Information still required from the Entra and platform teams

- Tenant ID and tenant domain.
- API app registration client ID.
- Confirmed API Application ID URI/audience.
- Confirmed delegated scope value.
- Swagger client ID.
- Redirect URI for each approved environment.
- Admin consent confirmation.
- Confirmation that the four business app roles use the configured values.
- User or AD-group assignments to each business app role.
- Step Functions/service app registration client ID.
- Approved machine credential method: secret, certificate, or workload identity federation.
- Assignment of `access_as_application` to the machine service principal.
- Environment hostnames and the environments in which Swagger may be enabled.

## Validation before enabling

Run these checks after inserting the real Entra values:

```text
dotnet restore
dotnet build RemediationTool.sln
dotnet test RemediationTool.sln
```

Then verify:

- No token returns `401`.
- Valid token without the required role returns `403`.
- Valid user token with the correct scope and role succeeds.
- Valid app-only token succeeds only on internal processing endpoints.
- Wrong issuer, audience, expired, or invalid-signature tokens are rejected.
- Swagger redirects to Entra, returns to `/swagger/oauth2-redirect.html`, and sends the access token.

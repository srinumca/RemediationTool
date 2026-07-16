# RemediationTool_POC

## Microsoft Entra ID authentication

The API supports Microsoft Entra ID JWT access-token validation and Swagger OAuth 2.0 Authorization Code flow with PKCE.

Authentication is disabled by default so the application can continue to run until the Entra application registrations and environment values are available.

### Required Entra registrations

Create two app registrations:

1. **GFR Remediation Tool API**
   - Single-tenant web API.
   - Expose a delegated scope such as `access_as_user` for Swagger and UI calls.
   - Expose an application role such as `access_as_application` for machine-to-machine callers.
   - The complete delegated scope normally looks like `api://<api-client-id>/access_as_user`.

2. **GFR Remediation Tool Swagger**
   - Public/browser client.
   - Add the API's delegated permission.
   - Configure Authorization Code flow with PKCE.
   - Do not configure or expose a client secret in Swagger.

For local development, register this redirect URI on the Swagger client:

```text
https://localhost:58207/swagger/oauth2-redirect.html
```

Add the equivalent redirect URI for each deployed environment.

### Configuration

Provide these values through environment-specific configuration or deployment variables:

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
```

When authentication is enabled, the application validates the token's issuer, audience, lifetime, and signature. The fallback authorization policy protects all mapped controller endpoints and then requires either:

- the delegated `access_as_user` scope for calls made on behalf of a user; or
- the `access_as_application` application role for daemon or service calls.

### Swagger login flow

1. Start the API in the Development environment.
2. Open `/swagger`.
3. Select **Authorize**.
4. Sign in using the Microsoft Entra account.
5. Approve the requested API scope when required.
6. Swagger sends the resulting access token as `Authorization: Bearer <token>` on API requests.

### Non-user callers

The ingestion endpoints are called by AWS Step Functions. Before enabling authentication in an environment, configure that machine-to-machine caller to obtain and send a valid Entra application token containing the configured `access_as_application` role, or place an approved token-validating gateway in front of the API. Otherwise, those calls will receive `401 Unauthorized` or `403 Forbidden`.

### Business roles

The current change establishes authenticated API access. Business-role policies for `System_Admin`, `Admin`, `User`, and `View_Only` should be added after the Entra app roles or AD group-to-role mapping is finalized.

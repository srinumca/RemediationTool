using Amazon.DynamoDBv2;
using Amazon.S3;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using RemediationTool.Application.Validators;
using RemediationTool.Infrastructure;
using RemediationTool.Infrastructure.DynamoDB;
using RemediationTool.Infrastructure.FileServices;
using RemediationTool.Infrastructure.Logging;
using RemediationTool.Infrastructure.Repositories;
using RemediationTool.Infrastructure.Strategies;
using Serilog;
using System.Text.Json.Serialization;

// ─── Serilog bootstrap logger ────────────────────────────────────────────────
// Captures any startup errors before the full app config is loaded.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("GFR Remediation Tool starting up...");

    var builder = WebApplication.CreateBuilder(args);

    var authenticationEnabled = builder.Configuration.GetValue<bool>("Authentication:Enabled");
    var azureAdTenantId = builder.Configuration["AzureAd:TenantId"] ?? string.Empty;
    var delegatedScope = builder.Configuration["AzureAd:Scopes"] ?? string.Empty;
    var applicationRole = builder.Configuration["AzureAd:ApplicationRole"] ?? string.Empty;
    var swaggerClientId = builder.Configuration["SwaggerAzureAd:ClientId"] ?? string.Empty;
    var swaggerScope = builder.Configuration["SwaggerAzureAd:Scope"] ?? string.Empty;

    if (authenticationEnabled)
    {
        var missingAuthenticationSettings = new[]
        {
            (Key: "AzureAd:TenantId", Value: azureAdTenantId),
            (Key: "AzureAd:ClientId", Value: builder.Configuration["AzureAd:ClientId"]),
            (Key: "AzureAd:Scopes", Value: delegatedScope),
            (Key: "AzureAd:ApplicationRole", Value: applicationRole),
            (Key: "SwaggerAzureAd:ClientId", Value: swaggerClientId),
            (Key: "SwaggerAzureAd:Scope", Value: swaggerScope)
        }
        .Where(setting => string.IsNullOrWhiteSpace(setting.Value))
        .Select(setting => setting.Key)
        .ToArray();

        if (missingAuthenticationSettings.Length > 0)
        {
            throw new InvalidOperationException(
                $"Microsoft Entra authentication is enabled, but these settings are missing: {string.Join(", ", missingAuthenticationSettings)}.");
        }
    }

    var ingestionProcessingOptions = builder.Configuration
        .GetSection(IngestionProcessingOptions.SectionName)
        .Get<IngestionProcessingOptions>() ?? new IngestionProcessingOptions();

    var maxUploadRequestBodySizeBytes = ingestionProcessingOptions.MaxUploadFileSizeBytes;

    // ─── Request size limits ──────────────────────────────────────────────────
    // ASP.NET Core rejects multipart/form-data uploads over the default limit
    // before the controller is reached. Keep Kestrel and multipart form limits
    // aligned with ingestion validation.
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = maxUploadRequestBodySizeBytes;
    });

    builder.Services.Configure<FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = maxUploadRequestBodySizeBytes;
    });

    // ─── Serilog — read config from appsettings.json ─────────────────────────
    // Replaces the default ASP.NET Core logging with Serilog.
    // All ILogger<T> calls throughout the app automatically write through Serilog.
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)   // reads "Serilog" section from appsettings.json
        .ReadFrom.Services(services)                      // allows enrichers registered in DI
        .Enrich.FromLogContext()                          // adds log context properties (e.g. RequestId)
        .Enrich.WithMachineName()                         // adds MachineName to every log entry
        .Enrich.WithEnvironmentName());                   // adds EnvironmentName (Development / Production)

    // ─── Authentication & authorization ───────────────────────────────────────
    // User calls must contain the configured delegated scope. Machine-to-machine
    // calls must contain the configured application role.
    if (authenticationEnabled)
    {
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
    }

    builder.Services.AddAuthorization(options =>
    {
        if (authenticationEnabled)
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder(
                    JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                {
                    var scopeClaim = context.User.FindFirst("scp")
                        ?? context.User.FindFirst("http://schemas.microsoft.com/identity/claims/scope");

                    var scopes = scopeClaim?.Value.Split(
                            ' ',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        ?? Array.Empty<string>();

                    return scopes.Contains(delegatedScope, StringComparer.OrdinalIgnoreCase)
                        || context.User.IsInRole(applicationRole);
                })
                .Build();
        }
    });

    // ─── Controllers ─────────────────────────────────────────────────────────
    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
        {
            opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

    // ─── Swagger ─────────────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "GFR Remediation Tool API", Version = "v1" });

        if (authenticationEnabled)
        {
            c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description = "Microsoft Entra ID authorization code flow with PKCE.",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri(
                            $"https://login.microsoftonline.com/{azureAdTenantId}/oauth2/v2.0/authorize"),
                        TokenUrl = new Uri(
                            $"https://login.microsoftonline.com/{azureAdTenantId}/oauth2/v2.0/token"),
                        Scopes = new Dictionary<string, string>
                        {
                            [swaggerScope] = "Access the GFR Remediation Tool API"
                        }
                    }
                }
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "oauth2"
                        }
                    },
                    new[] { swaggerScope }
                }
            });
        }
    });

    // ─── Validation ──────────────────────────────────────────────────────────
    builder.Services.AddValidatorsFromAssemblyContaining<FileFindingValidator>();

    // ─── Options ─────────────────────────────────────────────────────────────
    builder.Services.Configure<IngestionProcessingOptions>(
        builder.Configuration.GetSection(IngestionProcessingOptions.SectionName));
    builder.Services.Configure<QuarantineProcessingOptions>(
        builder.Configuration.GetSection(QuarantineProcessingOptions.SectionName));

    // ─── Storage ─────────────────────────────────────────────────────────────
    var storageType = builder.Configuration["Storage:Type"] ?? "Local";
    var useS3Storage = storageType.Equals("S3", StringComparison.OrdinalIgnoreCase);

    if (useS3Storage)
    {
        builder.Services.AddRemediationAwsServices(builder.Configuration);
        builder.Services.AddSingleton<IStorageService, S3StorageService>();
        builder.Services.AddSingleton<IQuarantineFileService, StorageQuarantineFileService>();
    }
    else
    {
        builder.Services.AddSingleton<IStorageService, LocalStorageService>();
        builder.Services.AddSingleton<IQuarantineFileService, LocalQuarantineFileService>();
    }

    // ─── Persistence ─────────────────────────────────────────────────────────
    var persistenceProvider = builder.Configuration["Persistence:Provider"] ?? "Json";

    if (persistenceProvider.Equals("DynamoDB", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddRemediationAwsServices(builder.Configuration);

        builder.Services.Configure<DynamoDbOptions>(
            builder.Configuration.GetSection(DynamoDbOptions.SectionName));

        // Keep the existing repositories as the read/single-record implementation.
        // The wrappers only replace high-volume batch write paths with bounded,
        // fully awaited concurrency and the same retry/idempotency behavior.
        builder.Services.AddSingleton<DynamoDbFileFindingRepository>();
        builder.Services.AddSingleton<IFileFindingRepository, ConcurrentDynamoDbFileFindingRepository>();
        builder.Services.AddSingleton<IIngestionJobAuditRepository, DynamoDbIngestionJobAuditRepository>();
        builder.Services.AddSingleton<DynamoDbRejectedRowRepository>();
        builder.Services.AddSingleton<IRejectedRowRepository, ConcurrentDynamoDbRejectedRowRepository>();
        builder.Services.AddSingleton<IIngestionCheckpointRepository, DynamoDbIngestionCheckpointRepository>();
        builder.Services.AddSingleton<DynamoDbIngestionStagingRepository>();
        builder.Services.AddSingleton<IIngestionStagingRepository, ConcurrentDynamoDbIngestionStagingRepository>();
        builder.Services.AddScoped<DynamoDbTableInitialiser>();
    }
    else
    {
        builder.Services.AddSingleton<IFileFindingRepository, JsonFileFindingRepository>();
        builder.Services.AddSingleton<IIngestionJobAuditRepository, JsonIngestionJobAuditRepository>();
        builder.Services.AddSingleton<IRejectedRowRepository, JsonRejectedRowRepository>();
        builder.Services.AddSingleton<IIngestionCheckpointRepository, JsonIngestionCheckpointRepository>();
        builder.Services.AddSingleton<IIngestionStagingRepository, JsonIngestionStagingRepository>();
    }

    // ─── Working file strategy (Parquet) ─────────────────────────────────────
    builder.Services.AddScoped<IIngestionWorkingFileStrategy, ParquetIngestionWorkingFileStrategy>();

    // ─── Application Services ─────────────────────────────────────────────────
    builder.Services.AddScoped<UploadService>();
    builder.Services.AddScoped<IngestionService>();
    builder.Services.AddScoped<QuarantineService>();
    builder.Services.AddScoped<RestoreService>();
    builder.Services.AddScoped<DeleteService>();
    builder.Services.AddScoped<ReportService>();
    builder.Services.AddSingleton<IAuditLogger, SerilogAuditLogger>();

    // ─── CORS (for dashboard) ─────────────────────────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
    });

    // ─────────────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ─── DynamoDB table initialisation ───────────────────────────────────────
    if (persistenceProvider.Equals("DynamoDB", StringComparison.OrdinalIgnoreCase))
    {
        using var scope = app.Services.CreateScope();
        var initialiser = scope.ServiceProvider.GetRequiredService<DynamoDbTableInitialiser>();
        await initialiser.InitialiseAsync();
    }

    // ─── Serilog HTTP request logging ────────────────────────────────────────
    // Logs every HTTP request: method, path, status code, and elapsed time.
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            if (authenticationEnabled)
            {
                options.OAuthClientId(swaggerClientId);
                options.OAuthUsePkce();
                options.OAuthScopes(swaggerScope);
                options.OAuthAppName("GFR Remediation Tool Swagger");
            }
        });
    }

    app.UseCors();
    app.UseStaticFiles();

    if (authenticationEnabled)
    {
        app.UseAuthentication();
    }

    app.UseAuthorization();
    app.MapControllers();

    Log.Information(
        "GFR Remediation Tool started successfully. Microsoft Entra authentication enabled: {AuthenticationEnabled}",
        authenticationEnabled);

    app.Run();
}
catch (Exception ex)
{
    // Fatal startup crash — written before Serilog is fully configured
    Log.Fatal(ex, "GFR Remediation Tool terminated unexpectedly during startup.");
}
finally
{
    // Flush all buffered log entries before process exits
    Log.CloseAndFlush();
}

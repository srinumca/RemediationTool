using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;
using RemediationTool.API.Authorization;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using RemediationTool.Application.Validators;
using RemediationTool.Infrastructure;
using RemediationTool.Infrastructure.DynamoDB;
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
    var swaggerEnabled = builder.Environment.IsDevelopment()
        || builder.Configuration.GetValue<bool>("Swagger:Enabled");
    var azureAdTenantId = builder.Configuration["AzureAd:TenantId"] ?? string.Empty;
    var swaggerClientId = builder.Configuration["SwaggerAzureAd:ClientId"] ?? string.Empty;
    var swaggerScope = builder.Configuration["SwaggerAzureAd:Scope"] ?? string.Empty;
    var swaggerAuthenticationEnabled = authenticationEnabled
        && !string.IsNullOrWhiteSpace(swaggerClientId)
        && !string.IsNullOrWhiteSpace(swaggerScope);

    if (authenticationEnabled)
    {
        var missingAuthenticationSettings = new[]
        {
            (Key: "AzureAd:TenantId", Value: azureAdTenantId),
            (Key: "AzureAd:ClientId", Value: builder.Configuration["AzureAd:ClientId"]),
            (Key: "AzureAd:Audience", Value: builder.Configuration["AzureAd:Audience"])
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
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = maxUploadRequestBodySizeBytes;
    });

    builder.Services.Configure<FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = maxUploadRequestBodySizeBytes;
    });

    // ─── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName());

    // ─── Authentication & authorization ───────────────────────────────────────
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
                .Build();
        }
    });

    builder.Services.AddRemediationAuthorizationPolicies(
        builder.Configuration,
        authenticationEnabled);

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

        if (swaggerAuthenticationEnabled)
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

    // ─── Storage ─────────────────────────────────────────────────────────────
    var storageType = builder.Configuration["Storage:Type"] ?? "Local";
    var useS3Storage = storageType.Equals("S3", StringComparison.OrdinalIgnoreCase);

    if (useS3Storage)
    {
        builder.Services.AddRemediationAwsServices(builder.Configuration);
        builder.Services.AddSingleton<IStorageService, S3StorageService>();
    }
    else
    {
        builder.Services.AddSingleton<IStorageService, LocalStorageService>();
    }

    // ─── Persistence ─────────────────────────────────────────────────────────
    var persistenceProvider = builder.Configuration["Persistence:Provider"] ?? "Json";

    if (persistenceProvider.Equals("DynamoDB", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddRemediationAwsServices(builder.Configuration);

        builder.Services.Configure<DynamoDbOptions>(
            builder.Configuration.GetSection(DynamoDbOptions.SectionName));

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

    // ─── Application services ─────────────────────────────────────────────────
    builder.Services.AddScoped<UploadService>();
    builder.Services.AddScoped<IngestionService>();
    builder.Services.AddSingleton<IAuditLogger, SerilogAuditLogger>();

    var app = builder.Build();

    // ─── DynamoDB table initialisation ───────────────────────────────────────
    if (persistenceProvider.Equals("DynamoDB", StringComparison.OrdinalIgnoreCase))
    {
        using var scope = app.Services.CreateScope();
        var initialiser = scope.ServiceProvider.GetRequiredService<DynamoDbTableInitialiser>();
        await initialiser.InitialiseAsync();
    }

    // ─── Serilog HTTP request logging ────────────────────────────────────────
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    });

    if (swaggerEnabled)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            if (swaggerAuthenticationEnabled)
            {
                options.OAuthClientId(swaggerClientId);
                options.OAuthUsePkce();
                options.OAuthScopes(swaggerScope);
                options.OAuthAppName("GFR Remediation Tool Swagger");
            }
        });
    }

    if (authenticationEnabled)
    {
        app.UseAuthentication();
    }

    app.UseAuthorization();
    app.MapControllers();

    Log.Information(
        "GFR Remediation Tool started successfully. Microsoft Entra authentication enabled: {AuthenticationEnabled}; Swagger enabled: {SwaggerEnabled}; Swagger OAuth enabled: {SwaggerAuthenticationEnabled}",
        authenticationEnabled,
        swaggerEnabled,
        swaggerAuthenticationEnabled);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "GFR Remediation Tool terminated unexpectedly during startup.");
}
finally
{
    Log.CloseAndFlush();
}

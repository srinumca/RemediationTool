using Amazon.DynamoDBv2;
using Amazon.S3;
using FluentValidation;
using Microsoft.AspNetCore.Http.Features;
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
    });

    // ─── Validation ──────────────────────────────────────────────────────────
    builder.Services.AddValidatorsFromAssemblyContaining<FileFindingValidator>();

    // ─── Options ─────────────────────────────────────────────────────────────
    builder.Services.Configure<IngestionProcessingOptions>(
        builder.Configuration.GetSection(IngestionProcessingOptions.SectionName));

    // ─── Storage ─────────────────────────────────────────────────────────────
    var storageType = builder.Configuration["Storage:Type"] ?? "Local";

    if (storageType.Equals("S3", StringComparison.OrdinalIgnoreCase))
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

        builder.Services.AddSingleton<IFileFindingRepository, DynamoDbFileFindingRepository>();
        builder.Services.AddSingleton<IIngestionJobAuditRepository, DynamoDbIngestionJobAuditRepository>();
        builder.Services.AddSingleton<IRejectedRowRepository, DynamoDbRejectedRowRepository>();
        builder.Services.AddSingleton<IIngestionCheckpointRepository, DynamoDbIngestionCheckpointRepository>();
        builder.Services.AddSingleton<IIngestionStagingRepository, DynamoDbIngestionStagingRepository>();
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
        app.UseSwaggerUI();
    }

    app.UseCors();
    app.UseStaticFiles();
    app.UseAuthorization();
    app.MapControllers();

    Log.Information("GFR Remediation Tool started successfully.");

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

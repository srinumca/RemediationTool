using Amazon.DynamoDBv2;
using Amazon.S3;
using FluentValidation;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using RemediationTool.Application.Validators;
using RemediationTool.Infrastructure;
using RemediationTool.Infrastructure.DynamoDB;
using RemediationTool.Infrastructure.Repositories;
using RemediationTool.Infrastructure.Strategies;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Core framework services
// ---------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---------------------------------------------------------------------------
// Application options
// ---------------------------------------------------------------------------
builder.Services.AddValidatorsFromAssemblyContaining<FileFindingValidator>();

builder.Services.Configure<IngestionProcessingOptions>(
    builder.Configuration.GetSection(IngestionProcessingOptions.SectionName));

// ---------------------------------------------------------------------------
// Persistence — config-driven switch
// Set "Persistence:Provider" to "DynamoDB" or "Json" in appsettings.json
// ---------------------------------------------------------------------------
var persistenceProvider = builder.Configuration["Persistence:Provider"] ?? "Json";
var isDynamo = persistenceProvider.Equals("DynamoDB", StringComparison.OrdinalIgnoreCase);

if (isDynamo)
{
    // Bind DynamoDB table name configuration
    builder.Services.Configure<DynamoDbOptions>(
        builder.Configuration.GetSection(DynamoDbOptions.SectionName));

    // Register AWS DynamoDB client
    builder.Services.AddAWSService<IAmazonDynamoDB>();

    // Register table initialiser — runs on startup, creates tables if missing
    builder.Services.AddSingleton<DynamoDbTableInitialiser>();

    // Register all 5 DynamoDB repositories
    builder.Services.AddSingleton<IFileFindingRepository, DynamoDbFileFindingRepository>();
    builder.Services.AddSingleton<IIngestionJobAuditRepository, DynamoDbIngestionJobAuditRepository>();
    builder.Services.AddSingleton<IRejectedRowRepository, DynamoDbRejectedRowRepository>();
    builder.Services.AddSingleton<IIngestionCheckpointRepository, DynamoDbIngestionCheckpointRepository>();
    builder.Services.AddSingleton<IIngestionStagingRepository, DynamoDbIngestionStagingRepository>();
}
else
{
    // Register all 5 JSON repositories
    builder.Services.AddSingleton<IFileFindingRepository, JsonFileFindingRepository>();
    builder.Services.AddSingleton<IIngestionJobAuditRepository, JsonIngestionJobAuditRepository>();
    builder.Services.AddSingleton<IRejectedRowRepository, JsonRejectedRowRepository>();
    builder.Services.AddSingleton<IIngestionCheckpointRepository, JsonIngestionCheckpointRepository>();
    builder.Services.AddSingleton<IIngestionStagingRepository, JsonIngestionStagingRepository>();
}

// ---------------------------------------------------------------------------
// Working file strategy (Parquet) — same for both persistence providers
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IIngestionWorkingFileStrategy, ParquetIngestionWorkingFileStrategy>();

// ---------------------------------------------------------------------------
// Storage — config-driven switch
// Set "Storage:Type" to "S3" or "Local" in appsettings.json
// ---------------------------------------------------------------------------
var storageType = builder.Configuration["Storage:Type"] ?? "Local";

if (storageType.Equals("S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddAWSService<IAmazonS3>();
    builder.Services.AddSingleton<IStorageService, S3StorageService>();
}
else
{
    builder.Services.AddSingleton<IStorageService, LocalStorageService>();
}

// ---------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<QuarantineService>();
builder.Services.AddScoped<RestoreService>();
builder.Services.AddScoped<DeleteService>();
builder.Services.AddScoped<ReportService>();

// ---------------------------------------------------------------------------
// Build app
// ---------------------------------------------------------------------------
var app = builder.Build();

// ---------------------------------------------------------------------------
// DynamoDB table initialisation — runs once on startup when using DynamoDB.
// Checks each table exists and creates it if not.
// In production, CDK has already created the tables — this is a no-op.
// ---------------------------------------------------------------------------
if (isDynamo)
{
    var initialiser = app.Services.GetRequiredService<DynamoDbTableInitialiser>();
    await initialiser.InitialiseAsync();
}

// ---------------------------------------------------------------------------
// Middleware pipeline
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.Run();
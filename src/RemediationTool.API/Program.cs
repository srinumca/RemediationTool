using FluentValidation;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using RemediationTool.Application.Validators;
using RemediationTool.Infrastructure;
using RemediationTool.Infrastructure.DynamoDB;
using RemediationTool.Infrastructure.Repositories;
using RemediationTool.Infrastructure.Strategies;
using Amazon.DynamoDBv2;
using RemediationTool.Infrastructure.DynamoDB;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Core framework
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

builder.Services.Configure<DynamoDbOptions>(
    builder.Configuration.GetSection(DynamoDbOptions.SectionName));

builder.Services.AddAWSService<IAmazonDynamoDB>();

// ---------------------------------------------------------------------------
// AWS clients — lazy registration, SSO-aware
// Mirrors AwsSampleApi/Infrastructure/AwsSetup.cs pattern exactly.
// Reads: AWS:Profile, AWS:SessionToken, AWS:AccessKey/SecretKey, or default chain.
// App starts WITHOUT credentials — credential errors happen at the endpoint level.
// ---------------------------------------------------------------------------
builder.Services.AddRemediationAwsServices(builder.Configuration);

// ---------------------------------------------------------------------------
// Persistence — config-driven switch (appsettings.json: Persistence:Provider)
// ---------------------------------------------------------------------------
var persistenceProvider = builder.Configuration["Persistence:Provider"] ?? "DynamoDB";
var isDynamo = persistenceProvider.Equals("DynamoDB", StringComparison.OrdinalIgnoreCase);

if (isDynamo)
{
    // Bind DynamoDB table names from appsettings AWS:DynamoDB section
    builder.Services.Configure<DynamoDbOptions>(
        builder.Configuration.GetSection(DynamoDbOptions.SectionName));

    // All 5 repositories — DynamoDB implementations
    builder.Services.AddSingleton<IFileFindingRepository, DynamoDbFileFindingRepository>();
    builder.Services.AddSingleton<IIngestionJobAuditRepository, DynamoDbIngestionJobAuditRepository>();
    builder.Services.AddSingleton<IRejectedRowRepository, DynamoDbRejectedRowRepository>();
    builder.Services.AddSingleton<IIngestionCheckpointRepository, DynamoDbIngestionCheckpointRepository>();
    builder.Services.AddSingleton<IIngestionStagingRepository, DynamoDbIngestionStagingRepository>();
}
else
{
    // All 5 repositories — JSON file implementations (local dev)
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
// Storage — config-driven switch (appsettings.json: Storage:Type)
// ---------------------------------------------------------------------------
var storageType = builder.Configuration["Storage:Type"] ?? "Local";

if (storageType.Equals("S3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IStorageService, S3StorageService>();
else
    builder.Services.AddSingleton<IStorageService, LocalStorageService>();

// ---------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<QuarantineService>();
builder.Services.AddScoped<RestoreService>();
builder.Services.AddScoped<DeleteService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
// ---------------------------------------------------------------------------
// Build
// ---------------------------------------------------------------------------
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.UseStaticFiles();
app.UseDefaultFiles();
app.MapControllers();
app.Run();
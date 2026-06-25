using Amazon.DynamoDBv2;
using Amazon.S3;
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
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ─── Controllers ────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// ─── Swagger ────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "GFR Remediation Tool API", Version = "v1" });
});

// ─── Validation ──────────────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssemblyContaining<FileFindingValidator>();

// ─── Options ─────────────────────────────────────────────────────────────────
builder.Services.Configure<IngestionProcessingOptions>(
    builder.Configuration.GetSection(IngestionProcessingOptions.SectionName));

// ─── Storage ─────────────────────────────────────────────────────────────────
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

// ─── Persistence ─────────────────────────────────────────────────────────────
var persistenceProvider = builder.Configuration["Persistence:Provider"] ?? "Json";

if (persistenceProvider.Equals("DynamoDB", StringComparison.OrdinalIgnoreCase))
{
    // DynamoDB — registers IAmazonDynamoDB lazily so app starts without credentials
    builder.Services.AddRemediationAwsServices(builder.Configuration);

    builder.Services.Configure<DynamoDbOptions>(
        builder.Configuration.GetSection(DynamoDbOptions.SectionName));

    builder.Services.AddSingleton<IFileFindingRepository, DynamoDbFileFindingRepository>();
    builder.Services.AddSingleton<IIngestionJobAuditRepository, DynamoDbIngestionJobAuditRepository>();
    builder.Services.AddSingleton<IRejectedRowRepository, DynamoDbRejectedRowRepository>();
    builder.Services.AddSingleton<IIngestionCheckpointRepository, DynamoDbIngestionCheckpointRepository>();
    builder.Services.AddSingleton<IIngestionStagingRepository, DynamoDbIngestionStagingRepository>();
}
else
{
    // JSON file repositories — local development only
    builder.Services.AddSingleton<IFileFindingRepository, JsonFileFindingRepository>();
    builder.Services.AddSingleton<IIngestionJobAuditRepository, JsonIngestionJobAuditRepository>();
    builder.Services.AddSingleton<IRejectedRowRepository, JsonRejectedRowRepository>();
    builder.Services.AddSingleton<IIngestionCheckpointRepository, JsonIngestionCheckpointRepository>();
    builder.Services.AddSingleton<IIngestionStagingRepository, JsonIngestionStagingRepository>();
}

// ─── Working file strategy (Parquet) ─────────────────────────────────────────
builder.Services.AddScoped<IIngestionWorkingFileStrategy, ParquetIngestionWorkingFileStrategy>();

// ─── Application Services ────────────────────────────────────────────────────
builder.Services.AddScoped<UploadService>();       // Upload API — file save + record create only
builder.Services.AddScoped<IngestionService>();    // Ingestion API — row processing
builder.Services.AddScoped<QuarantineService>();
builder.Services.AddScoped<RestoreService>();
builder.Services.AddScoped<DeleteService>();
builder.Services.AddScoped<ReportService>();

// ─── CORS (for dashboard) ─────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseStaticFiles();
app.UseAuthorization();
app.MapControllers();

app.Run();
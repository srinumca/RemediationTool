using Amazon.S3;
using FluentValidation;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using RemediationTool.Application.Validators;
using RemediationTool.Infrastructure;
using RemediationTool.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Validators
builder.Services.AddValidatorsFromAssemblyContaining<FileFindingValidator>();

// Repository / Persistence configuration
var persistenceProvider = builder.Configuration["Persistence:Provider"] ?? "Json";

if (persistenceProvider.Equals("DynamoDB", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IFileFindingRepository, DynamoDbFileFindingRepository>();
    builder.Services.AddSingleton<IIngestionJobAuditRepository, DynamoDbIngestionJobAuditRepository>();
    builder.Services.AddSingleton<IRejectedRowRepository, DynamoDbRejectedRowRepository>();
}
else
{
    builder.Services.AddSingleton<IFileFindingRepository, JsonFileFindingRepository>();
    builder.Services.AddSingleton<IIngestionJobAuditRepository, JsonIngestionJobAuditRepository>();
    builder.Services.AddSingleton<IRejectedRowRepository, JsonRejectedRowRepository>();
}

// Application services
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<QuarantineService>();
builder.Services.AddScoped<RestoreService>();
builder.Services.AddScoped<DeleteService>();
builder.Services.AddScoped<ReportService>();

// Storage configuration
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

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
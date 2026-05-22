using Amazon.S3;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using RemediationTool.Infrastructure;
using RemediationTool.Infrastructure.Repositories;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// 🔹 Add Controllers
builder.Services.AddControllers();

// 🔹 Dependency Injection
builder.Services.AddSingleton<IFileFindingRepository, JsonFileFindingRepository>();
builder.Services.AddScoped<QuarantineService>();
builder.Services.AddSingleton<IStorageService, LocalStorageService>();
builder.Services.AddScoped<DeleteService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddScoped<IngestionService>();
//builder.Services.AddSingleton<IStorageService, LocalStorageService>();
builder.Services.AddSingleton<IFileFindingRepository, JsonFileFindingRepository>();
builder.Services.AddScoped<RestoreService>();
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddSingleton<IStorageService, S3StorageService>();
builder.Services.AddValidatorsFromAssemblyContaining<FileFindingValidator>();
// 🔹 Swagger (for testing APIs)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 🔹 Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
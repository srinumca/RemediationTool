using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonIngestionJobAuditRepository : IIngestionJobAuditRepository
{
    private static readonly JsonSerializerOptions ReadOptions = CreateSerializerOptions(writeIndented: false);
    private static readonly JsonSerializerOptions WriteOptions = CreateSerializerOptions(writeIndented: true);

    private readonly string _filePath;
    private readonly object _lock = new();

    public JsonIngestionJobAuditRepository(IConfiguration configuration)
    {
        var rootPath = configuration["Persistence:JsonRootPath"] ?? "storage";
        _filePath = Path.Combine(rootPath, "ingestion-job-audit.json");

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (!File.Exists(_filePath))
            JsonFileHelper.WriteAllText(_filePath, "[]");
    }

    public List<IngestionJobAudit> GetAll()
    {
        lock (_lock)
        {
            return ReadAllInternal();
        }
    }

    public IngestionJobAudit? GetByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return null;

        lock (_lock)
        {
            return ReadAllInternal().Find(audit =>
                string.Equals(audit.JobId, jobId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Add(IngestionJobAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        lock (_lock)
        {
            var audits = ReadAllInternal();
            audits.Add(audit);
            WriteAllInternal(audits);
        }
    }

    public void Update(IngestionJobAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        lock (_lock)
        {
            var audits = ReadAllInternal();
            var index = audits.FindIndex(existing =>
                string.Equals(existing.JobId, audit.JobId, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
                audits[index] = audit;
            else
                audits.Add(audit);

            WriteAllInternal(audits);
        }
    }

    private List<IngestionJobAudit> ReadAllInternal()
    {
        var json = JsonFileHelper.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<IngestionJobAudit>();

        return JsonSerializer.Deserialize<List<IngestionJobAudit>>(json, ReadOptions)
               ?? new List<IngestionJobAudit>();
    }

    private void WriteAllInternal(List<IngestionJobAudit> audits)
    {
        var json = JsonSerializer.Serialize(audits, WriteOptions);
        JsonFileHelper.WriteAllText(_filePath, json);
    }

    private static JsonSerializerOptions CreateSerializerOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

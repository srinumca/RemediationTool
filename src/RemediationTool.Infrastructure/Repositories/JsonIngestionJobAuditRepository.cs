using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonIngestionJobAuditRepository : IIngestionJobAuditRepository
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public JsonIngestionJobAuditRepository(IConfiguration configuration)
    {
        var rootPath = configuration["Persistence:JsonRootPath"] ?? "storage";
        _filePath = Path.Combine(rootPath, "ingestion-job-audit.json");

        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(_filePath))
        {
            File.WriteAllText(_filePath, "[]");
        }
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
        return GetAll()
            .FirstOrDefault(x => x.JobId.Equals(jobId, StringComparison.OrdinalIgnoreCase));
    }

    public void Add(IngestionJobAudit audit)
    {
        lock (_lock)
        {
            var audits = ReadAllInternal();
            audits.Add(audit);
            WriteAllInternal(audits);
        }
    }

    public void Update(IngestionJobAudit audit)
    {
        lock (_lock)
        {
            var audits = ReadAllInternal();

            var index = audits.FindIndex(x =>
                x.JobId.Equals(audit.JobId, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                audits[index] = audit;
            }
            else
            {
                audits.Add(audit);
            }

            WriteAllInternal(audits);
        }
    }

    private List<IngestionJobAudit> ReadAllInternal()
    {
        var json = File.ReadAllText(_filePath);

        return JsonSerializer.Deserialize<List<IngestionJobAudit>>(
                   json,
                   new JsonSerializerOptions
                   {
                       Converters = { new JsonStringEnumConverter() }
                   })
               ?? new List<IngestionJobAudit>();
    }

    private void WriteAllInternal(List<IngestionJobAudit> audits)
    {
        var json = JsonSerializer.Serialize(
            audits,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });

        File.WriteAllText(_filePath, json);
    }
}
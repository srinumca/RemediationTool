using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonRejectedRowRepository : IRejectedRowRepository
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public JsonRejectedRowRepository(IConfiguration configuration)
    {
        var rootPath = configuration["Persistence:JsonRootPath"] ?? "storage";
        _filePath = Path.Combine(rootPath, "rejected-rows.json");

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

    public List<RejectedRowDetail> GetAll()
    {
        lock (_lock)
        {
            return ReadAllInternal();
        }
    }

    public List<RejectedRowDetail> GetByJobId(string jobId)
    {
        return GetAll()
            .Where(x => x.JobId.Equals(jobId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void AddRange(List<RejectedRowDetail> rejectedRows)
    {
        if (rejectedRows == null || rejectedRows.Count == 0)
            return;

        lock (_lock)
        {
            var existingRows = ReadAllInternal();
            existingRows.AddRange(rejectedRows);
            WriteAllInternal(existingRows);
        }
    }

    private List<RejectedRowDetail> ReadAllInternal()
    {
        var json = File.ReadAllText(_filePath);

        return JsonSerializer.Deserialize<List<RejectedRowDetail>>(json)
               ?? new List<RejectedRowDetail>();
    }

    private void WriteAllInternal(List<RejectedRowDetail> rejectedRows)
    {
        var json = JsonSerializer.Serialize(
            rejectedRows,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(_filePath, json);
    }
}
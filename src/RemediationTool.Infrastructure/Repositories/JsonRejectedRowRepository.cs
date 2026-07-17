using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonRejectedRowRepository : IRejectedRowRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly object _lock = new();

    public JsonRejectedRowRepository(IConfiguration configuration)
    {
        var rootPath = configuration["Persistence:JsonRootPath"] ?? "storage";
        _filePath = Path.Combine(rootPath, "rejected-rows.json");

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (!File.Exists(_filePath))
            JsonFileHelper.WriteAllText(_filePath, "[]");
    }

    public void AddRange(List<RejectedRowDetail> rejectedRows)
    {
        if (rejectedRows == null || rejectedRows.Count == 0)
            return;

        lock (_lock)
        {
            var existingRows = ReadAllInternal();
            existingRows.EnsureCapacity(existingRows.Count + rejectedRows.Count);

            foreach (var row in rejectedRows)
                existingRows.Add(row);

            WriteAllInternal(existingRows);
        }
    }

    private List<RejectedRowDetail> ReadAllInternal()
    {
        var json = JsonFileHelper.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<RejectedRowDetail>();

        return JsonSerializer.Deserialize<List<RejectedRowDetail>>(json, JsonOptions)
               ?? new List<RejectedRowDetail>();
    }

    private void WriteAllInternal(List<RejectedRowDetail> rejectedRows)
    {
        var json = JsonSerializer.Serialize(rejectedRows, JsonOptions);
        JsonFileHelper.WriteAllText(_filePath, json);
    }
}

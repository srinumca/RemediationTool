using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonRejectedRowRepository : IRejectedRowRepository
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly ILogger<JsonRejectedRowRepository> _logger;

    public JsonRejectedRowRepository(IConfiguration configuration, ILogger<JsonRejectedRowRepository> logger)
    {
        _logger = logger;
        var rootPath = configuration["Persistence:JsonRootPath"] ?? "storage";
        _filePath = Path.Combine(rootPath, "rejected-rows.json");

        _logger.LogInformation("JsonRejectedRowRepository initialized with FilePath: {FilePath}", _filePath);

        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(_filePath))
        {
            JsonFileHelper.WriteAllText(_filePath, "[]");
        }
    }

    /// <summary>
    /// Gets all rejected row details from the JSON file.
    /// </summary>
    /// <returns></returns>
    public List<RejectedRowDetail> GetAll()
    {
        _logger.LogDebug("Getting all RejectedRowDetail records");
        lock (_lock)
        {
            var results = ReadAllInternal();
            _logger.LogDebug("Retrieved {Count} RejectedRowDetail records", results.Count);
            return results;
        }
    }

    /// <summary>
    /// Gets rejected row details by JobId from the JSON file.
    /// </summary>
    /// <param name="jobId"></param>
    /// <returns></returns>
    public List<RejectedRowDetail> GetByJobId(string jobId)
    {
        _logger.LogDebug("Getting RejectedRowDetail records by JobId: {JobId}", jobId);
        return GetAll()
            .Where(x => x.JobId.Equals(jobId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Adds a range of rejected row details to the JSON file.
    /// </summary>
    /// <param name="rejectedRows"></param>
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

    /// <summary>
    /// Reads all rejected row details from the JSON file.
    /// </summary>
    /// <returns></returns>
    private List<RejectedRowDetail> ReadAllInternal()
    {
        var json = JsonFileHelper.ReadAllText(_filePath);

        if (string.IsNullOrWhiteSpace(json))
            return new List<RejectedRowDetail>();

        return JsonSerializer.Deserialize<List<RejectedRowDetail>>(json)
               ?? new List<RejectedRowDetail>();
    }

    /// <summary>
    /// Writes all rejected row details to the JSON file.
    /// </summary>
    /// <param name="rejectedRows"></param>
    private void WriteAllInternal(List<RejectedRowDetail> rejectedRows)
    {
        var json = JsonSerializer.Serialize(
            rejectedRows,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        JsonFileHelper.WriteAllText(_filePath, json);
    }
}
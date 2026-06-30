using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonIngestionJobAuditRepository : IIngestionJobAuditRepository
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly ILogger<JsonIngestionJobAuditRepository> _logger;

    public JsonIngestionJobAuditRepository(IConfiguration configuration, ILogger<JsonIngestionJobAuditRepository> logger)
    {
        _logger = logger;
        var rootPath = configuration["Persistence:JsonRootPath"] ?? "storage";
        _filePath = Path.Combine(rootPath, "ingestion-job-audit.json");

        _logger.LogInformation("JsonIngestionJobAuditRepository initialized with FilePath: {FilePath}", _filePath);

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
    /// Gets all IngestionJobAudit records from the JSON file.
    /// </summary>
    /// <returns></returns>
    public List<IngestionJobAudit> GetAll()
    {
        _logger.LogDebug("Getting all IngestionJobAudit records");
        lock (_lock)
        {
            var results = ReadAllInternal();
            _logger.LogDebug("Retrieved {Count} IngestionJobAudit records", results.Count);
            return results;
        }
    }

    /// <summary>
    /// Gets an IngestionJobAudit record by JobId from the JSON file.
    /// </summary>
    /// <param name="jobId"></param>
    /// <returns></returns>
    public IngestionJobAudit? GetByJobId(string jobId)
    {
        _logger.LogDebug("Getting IngestionJobAudit by JobId: {JobId}", jobId);
        var result = GetAll()
            .FirstOrDefault(x => x.JobId.Equals(jobId, StringComparison.OrdinalIgnoreCase));
        if (result != null)
            _logger.LogDebug("IngestionJobAudit found. JobId: {JobId}, Status: {Status}", jobId, result.Status);
        else
            _logger.LogDebug("IngestionJobAudit not found. JobId: {JobId}", jobId);
        return result;
    }

    /// <summary>
    /// Adds a new IngestionJobAudit record to the JSON file.
    /// </summary>
    /// <param name="audit"></param>
    public void Add(IngestionJobAudit audit)
    {
        _logger.LogDebug("Adding IngestionJobAudit. JobId: {JobId}, Status: {Status}", audit.JobId, audit.Status);
        lock (_lock)
        {
            var audits = ReadAllInternal();
            audits.Add(audit);
            WriteAllInternal(audits);
            _logger.LogDebug("IngestionJobAudit added successfully. JobId: {JobId}, TotalAudits: {Total}", audit.JobId, audits.Count);
        }
    }

    /// <summary>
    /// Updates an existing IngestionJobAudit record in the JSON file. If the record does not exist, it adds it as a new record.
    /// </summary>
    /// <param name="audit"></param>
    public void Update(IngestionJobAudit audit)
    {
        _logger.LogDebug("Updating IngestionJobAudit. JobId: {JobId}, Status: {Status}", audit.JobId, audit.Status);
        lock (_lock)
        {
            var audits = ReadAllInternal();

            var index = audits.FindIndex(x =>
                x.JobId.Equals(audit.JobId, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                audits[index] = audit;
                _logger.LogDebug("IngestionJobAudit updated at index {Index}", index);
            }
            else
            {
                audits.Add(audit);
                _logger.LogDebug("IngestionJobAudit not found for update, adding as new. JobId: {JobId}", audit.JobId);
            }

            WriteAllInternal(audits);
        }
    }

    /// <summary>
    /// Reads all IngestionJobAudit records from the JSON file and deserializes them into a list.
    /// </summary>
    /// <returns></returns>
    private List<IngestionJobAudit> ReadAllInternal()
    {
        var json = JsonFileHelper.ReadAllText(_filePath);

        if (string.IsNullOrWhiteSpace(json))
            return new List<IngestionJobAudit>();

        return JsonSerializer.Deserialize<List<IngestionJobAudit>>(
                   json,
                   new JsonSerializerOptions
                   {
                       Converters = { new JsonStringEnumConverter() }
                   })
               ?? new List<IngestionJobAudit>();
    }

    /// <summary>
    /// Writes all IngestionJobAudit records to the JSON file by serializing the list into JSON format.
    /// </summary>
    /// <param name="audits"></param>
    private void WriteAllInternal(List<IngestionJobAudit> audits)
    {
        var json = JsonSerializer.Serialize(
            audits,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });

        JsonFileHelper.WriteAllText(_filePath, json);
    }
}
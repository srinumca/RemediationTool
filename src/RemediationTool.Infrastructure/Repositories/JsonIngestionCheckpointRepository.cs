using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonIngestionCheckpointRepository : IIngestionCheckpointRepository
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<JsonIngestionCheckpointRepository> _logger;

    public JsonIngestionCheckpointRepository(ILogger<JsonIngestionCheckpointRepository> logger)
    {
        _logger = logger;
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);

        _filePath = Path.Combine(dataDirectory, "ingestion-checkpoints.json");

        _logger.LogInformation("JsonIngestionCheckpointRepository initialized with FilePath: {FilePath}", _filePath);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <summary>
    /// Gets the IngestionCheckpoint by JobId. Returns null if not found.
    /// </summary>
    /// <param name="jobId"></param>
    /// <returns></returns>
    public IngestionCheckpoint? GetByJobId(string jobId)
    {
        _logger.LogDebug("Getting IngestionCheckpoint by JobId: {JobId}", jobId);
        if (string.IsNullOrWhiteSpace(jobId))
        {
            _logger.LogWarning("GetByJobId called with null or empty JobId");
            return null;
        }

        var checkpoints = LoadAll();
        var result = checkpoints.FirstOrDefault(checkpoint =>
            string.Equals(checkpoint.JobId, jobId, StringComparison.OrdinalIgnoreCase));

        if (result != null)
            _logger.LogDebug("IngestionCheckpoint found. JobId: {JobId}, Status: {Status}", jobId, result.Status);
        else
            _logger.LogDebug("IngestionCheckpoint not found. JobId: {JobId}", jobId);

        return result;
    }

    /// <summary>
    /// Upserts the IngestionCheckpoint. If a checkpoint with the same JobId exists, it updates it; otherwise, it adds a new one.
    /// </summary>
    /// <param name="checkpoint"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public void Upsert(IngestionCheckpoint checkpoint)
    {
        _logger.LogDebug("Upserting IngestionCheckpoint. JobId: {JobId}, Status: {Status}", checkpoint?.JobId, checkpoint?.Status);
        if (checkpoint == null)
            throw new ArgumentNullException(nameof(checkpoint));

        var checkpoints = LoadAll();

        var existingIndex = checkpoints.FindIndex(existing =>
            string.Equals(existing.JobId, checkpoint.JobId, StringComparison.OrdinalIgnoreCase));

        checkpoint.LastCheckpointUtc = DateTime.UtcNow;

        if (existingIndex >= 0)
        {
            checkpoints[existingIndex] = checkpoint;
        }
        else
        {
            checkpoint.CreatedAtUtc = DateTime.UtcNow;
            checkpoints.Add(checkpoint);
        }

        SaveAll(checkpoints);
    }

    /// <summary>
    /// Loads all IngestionCheckpoints from the JSON file. If the file does not exist or is empty, returns an empty list.
    /// </summary>
    /// <returns></returns>
    private List<IngestionCheckpoint> LoadAll()
    {
        if (!File.Exists(_filePath))
            return new List<IngestionCheckpoint>();

        var json = File.ReadAllText(_filePath);

        if (string.IsNullOrWhiteSpace(json))
            return new List<IngestionCheckpoint>();

        return JsonSerializer.Deserialize<List<IngestionCheckpoint>>(json, _jsonOptions)
               ?? new List<IngestionCheckpoint>();
    }

    /// <summary>
    /// Saves all IngestionCheckpoints to the JSON file, overwriting any existing content.
    /// </summary>
    /// <param name="checkpoints"></param>
    private void SaveAll(List<IngestionCheckpoint> checkpoints)
    {
        var json = JsonSerializer.Serialize(checkpoints, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
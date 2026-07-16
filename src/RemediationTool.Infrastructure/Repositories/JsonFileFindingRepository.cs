using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// JSON file-backed finding persistence used for local development.
/// </summary>
public sealed class JsonFileFindingRepository : IFileFindingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _filePath;
    private readonly object _lock = new();

    public JsonFileFindingRepository(IConfiguration configuration)
    {
        var rootPath = configuration["Persistence:JsonRootPath"] ?? "storage";
        _filePath = Path.Combine(rootPath, "metadata.json");

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (!File.Exists(_filePath))
            JsonFileHelper.WriteAllText(_filePath, "[]");
    }

    public void AddRange(IReadOnlyList<FileFinding> findings)
    {
        if (findings == null || findings.Count == 0)
            return;

        lock (_lock)
        {
            var all = ReadAll();
            all.EnsureCapacity(all.Count + findings.Count);

            foreach (var finding in findings)
                all.Add(finding);

            WriteAll(all);
        }
    }

    public IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId)
    {
        if (string.IsNullOrWhiteSpace(ingestionJobId))
            return Array.Empty<FileFinding>();

        lock (_lock)
        {
            var result = new List<FileFinding>();

            foreach (var finding in ReadAll())
            {
                if (string.Equals(
                        finding.IngestionJobId,
                        ingestionJobId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(finding);
                }
            }

            result.Sort(static (left, right) => left.LoadDateUtc.CompareTo(right.LoadDateUtc));
            return result;
        }
    }

    private List<FileFinding> ReadAll()
    {
        var json = JsonFileHelper.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<FileFinding>();

        var trimmed = json.AsSpan().Trim();
        if (trimmed.SequenceEqual("[]".AsSpan()))
            return new List<FileFinding>();

        return JsonSerializer.Deserialize<List<FileFinding>>(json, JsonOptions)
               ?? new List<FileFinding>();
    }

    private void WriteAll(List<FileFinding> findings)
        => JsonFileHelper.WriteAllText(
            _filePath,
            JsonSerializer.Serialize(findings, JsonOptions));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

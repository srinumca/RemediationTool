using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonFileFindingRepository : IFileFindingRepository
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public JsonFileFindingRepository(IConfiguration configuration)
    {
        var rootPath = configuration["Persistence:JsonRootPath"] ?? "storage";
        _filePath = Path.Combine(rootPath, "metadata.json");

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

    public List<FileFinding> GetAll()
    {
        lock (_lock)
        {
            var json = File.ReadAllText(_filePath);

            return JsonSerializer.Deserialize<List<FileFinding>>(json)
                   ?? new List<FileFinding>();
        }
    }

    public FileFinding? GetById(Guid id)
    {
        return GetAll().FirstOrDefault(x => x.Id == id);
    }

    public void Add(FileFinding finding)
    {
        lock (_lock)
        {
            var findings = ReadAllInternal();
            findings.Add(finding);
            WriteAllInternal(findings);
        }
    }

    public void AddRange(List<FileFinding> findingsToAdd)
    {
        if (findingsToAdd == null || findingsToAdd.Count == 0)
            return;

        lock (_lock)
        {
            var findings = ReadAllInternal();
            findings.AddRange(findingsToAdd);
            WriteAllInternal(findings);
        }
    }

    public void Update(FileFinding finding)
    {
        lock (_lock)
        {
            var findings = ReadAllInternal();

            var index = findings.FindIndex(x => x.Id == finding.Id);

            if (index >= 0)
            {
                findings[index] = finding;
            }
            else
            {
                findings.Add(finding);
            }

            WriteAllInternal(findings);
        }
    }

    private List<FileFinding> ReadAllInternal()
    {
        var json = File.ReadAllText(_filePath);

        return JsonSerializer.Deserialize<List<FileFinding>>(json)
               ?? new List<FileFinding>();
    }

    private void WriteAllInternal(List<FileFinding> findings)
    {
        var json = JsonSerializer.Serialize(
            findings,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(_filePath, json);
    }
}
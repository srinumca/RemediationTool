using System.Text.Json;
using RemediationTool.Domain;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonFileFindingRepository : IFileFindingRepository
{
    private readonly string _filePath = Path.Combine("storage", "metadata.json");
    private List<FileFinding> _cache = new();

    public JsonFileFindingRepository()
    {
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _cache = new List<FileFinding>();
            return;
        }

        var json = File.ReadAllText(_filePath);

        if (!string.IsNullOrWhiteSpace(json))
        {
            _cache = JsonSerializer.Deserialize<List<FileFinding>>(json) ?? new();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory("storage");

        var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_filePath, json);
    }

    public List<FileFinding> GetAll()
    {
        return _cache;
    }

    public void AddRange(List<FileFinding> records)
    {
        _cache.AddRange(records);
        Save();
    }

    public void Update(FileFinding record)
    {
        var existing = _cache.FirstOrDefault(x => x.FileName == record.FileName);

        if (existing != null)
        {
            existing.Status = record.Status;
            existing.QuarantinePath = record.QuarantinePath;
            existing.LastModifiedDate = record.LastModifiedDate;
            existing.FindingType = record.FindingType;
        }

        Save();
    }
}
using System.Text.Json;
using Microsoft.Extensions.Options;
using RemediationTool.Domain;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonFileFindingRepository : IFileFindingRepository
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<FileFinding> _cache = new();

    public JsonFileFindingRepository(IOptions<JsonFileRepositoryOptions> options)
    {
        _filePath = options?.Value?.FilePath ?? Path.Combine("storage", "metadata.json");
        Load();
    }

    private void Load()
    {
        lock (_lock)
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
    }

    private void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? "storage");

            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_filePath, json);
        }
    }

    public List<FileFinding> GetAll()
    {
        lock (_lock)
        {
            return _cache.Select(f => Clone(f)).ToList();
        }
    }

    public void AddRange(List<FileFinding> records)
    {
        if (records == null || records.Count == 0) return;

        lock (_lock)
        {
            foreach (var f in records)
            {
                if (string.IsNullOrWhiteSpace(f.Id))
                    f.Id = Guid.NewGuid().ToString();

                var existing = _cache.FirstOrDefault(x => x.Id == f.Id);
                if (existing == null)
                {
                    _cache.Add(Clone(f));
                }
                else
                {
                    existing.FileName = f.FileName;
                    //existing.FilePath = f.FilePath;
                    //existing.SourceSystem = f.SourceSystem;
                    //existing.FileSize = f.FileSize;
                    existing.LastModifiedDate = f.LastModifiedDate;
                    existing.Status = f.Status;
                    existing.QuarantinePath = f.QuarantinePath;
                }
            }

            Save();
        }
    }

    public void Update(FileFinding record)
    {
        if (record == null) return;

        lock (_lock)
        {
            var existing = _cache.FirstOrDefault(x => x.Id == record.Id);
            if (existing == null && !string.IsNullOrWhiteSpace(record.FileName))
                existing = _cache.FirstOrDefault(x => x.FileName == record.FileName);

            if (existing != null)
            {
                existing.FileName = record.FileName;
                existing.FilePath = record.FilePath;
                existing.SourceSystem = record.SourceSystem;
                existing.FileSize = record.FileSize;
                existing.LastModifiedDate = record.LastModifiedDate;
                existing.Status = record.Status;
                existing.QuarantinePath = record.QuarantinePath;

                Save();
            }
        }
    }

    private static FileFinding Clone(FileFinding f) => new()
    {
        //Id = f.Id,
        //FileName = f.FileName,
        //FilePath = f.FilePath,
        //SourceSystem = f.SourceSystem,
        //FileSize = f.FileSize,
        LastModifiedDate = f.LastModifiedDate,
        Status = f.Status,
        QuarantinePath = f.QuarantinePath
    };
}

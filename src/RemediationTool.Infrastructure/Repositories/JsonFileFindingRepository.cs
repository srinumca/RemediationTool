using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonFileFindingRepository : IFileFindingRepository
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<FileFinding> _cache = new();

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() } // 🔥 IMPORTANT
    };

    public JsonFileFindingRepository(IOptions<JsonFileRepositoryOptions> options)
    {
        _filePath = Path.Combine(Directory.GetCurrentDirectory(), "storage", "metadata.json");
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

            if (string.IsNullOrWhiteSpace(json))
            {
                _cache = new List<FileFinding>();
            }
            else
            {
                try
                {
                    _cache = JsonSerializer.Deserialize<List<FileFinding>>(json) ?? new();
                }
                catch
                {
                    _cache = new List<FileFinding>(); // fallback
                }
            }
        }
    }

    private void Save()
    {
        lock (_lock)
        {
            var dir = Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

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
                if (f.Id == Guid.Empty)
                    f.Id = Guid.NewGuid();

                var existing = _cache.FirstOrDefault(x => x.Id == f.Id);

                if (existing == null)
                {
                    _cache.Add(Clone(f));
                }
                else
                {
                    existing.FileName = f.FileName;
                    existing.FilePath = f.FilePath;
                    existing.SourceSystem = f.SourceSystem;
                    existing.FileSize = f.FileSize;
                    existing.FindingType = f.FindingType;
                    existing.LastModifiedDate = f.LastModifiedDate;
                    existing.Status = f.Status;
                    existing.QuarantinePath = f.QuarantinePath;
                    existing.IngestionId = f.IngestionId;
                    existing.InboundFileName = f.InboundFileName;
                    existing.UploadedBy = f.UploadedBy;
                    existing.LoadDate = f.LoadDate;
                    existing.UpdatedDate = f.UpdatedDate;
                    existing.IsValid = f.IsValid;
                    existing.ErrorReason = f.ErrorReason;
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

            if (existing != null)
            {
                existing.FileName = record.FileName;
                existing.FilePath = record.FilePath;
                existing.SourceSystem = record.SourceSystem;
                existing.FileSize = record.FileSize;
                existing.FindingType = record.FindingType;
                existing.LastModifiedDate = record.LastModifiedDate;
                existing.Status = record.Status;
                existing.QuarantinePath = record.QuarantinePath;
                existing.IngestionId = record.IngestionId;
                existing.InboundFileName = record.InboundFileName;
                existing.UploadedBy = record.UploadedBy;
                existing.LoadDate = record.LoadDate;
                existing.UpdatedDate = DateTime.UtcNow;
                existing.IsValid = record.IsValid;
                existing.ErrorReason = record.ErrorReason;

                Save();
            }
        }
    }

    private static FileFinding Clone(FileFinding f) => new()
    {
        Id = f.Id,
        FileName = f.FileName,
        FilePath = f.FilePath,
        SourceSystem = f.SourceSystem,
        FileSize = f.FileSize,
        FindingType = f.FindingType,
        LastModifiedDate = f.LastModifiedDate,
        Status = f.Status,
        QuarantinePath = f.QuarantinePath,
        IngestionId = f.IngestionId,
        InboundFileName = f.InboundFileName,
        UploadedBy = f.UploadedBy,
        LoadDate = f.LoadDate,
        UpdatedDate = f.UpdatedDate,
        IsValid = f.IsValid,
        ErrorReason = f.ErrorReason
    };
}
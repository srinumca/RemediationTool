using ClosedXML.Excel;
using CsvHelper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain;
using System.Globalization;

namespace RemediationTool.Application.Services;

public class IngestionService
{
    private readonly ILogger<IngestionService> _logger;
    private readonly IFileFindingRepository _repository;
    private readonly IStorageService _storage;

    public IngestionService(
        ILogger<IngestionService> logger,
        IFileFindingRepository repository,
        IStorageService storage)
    {
        _logger = logger;
        _repository = repository;
        _storage = storage;
    }

    public async Task<int> ProcessAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new Exception("Invalid file");

        var key = $"input/{file.FileName}";

        // Save file
        await _storage.UploadAsync(key, file.OpenReadStream());

        List<FileFinding> findings;
        var ext = Path.GetExtension(file.FileName).ToLower();

        using var stream = file.OpenReadStream();

        if (ext == ".xlsx")
            findings = ParseExcel(stream);
        else if (ext == ".csv")
            findings = ParseCsv(stream);
        else
            throw new Exception("Unsupported file format");

        _repository.AddRange(findings);

        return findings.Count;
    }

    private List<FileFinding> ParseExcel(Stream stream)
    {
        var findings = new List<FileFinding>();

        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            try
            {
                var fileName = row.Cell(1).GetString();
                var filePath = row.Cell(2).GetString();
                var dateText = row.Cell(3).GetString();
                var source = row.Cell(4).GetString();
                var fileSizeText = row.Cell(5).GetString();

                // ✅ Safe parsing
                DateTime.TryParse(dateText, out var lastModified);
                long.TryParse(fileSizeText, out var fileSize);

                findings.Add(new FileFinding
                {
                    Id = Guid.NewGuid(),
                    FileName = fileName,
                    FilePath = filePath,
                    LastModifiedDate = lastModified,
                    SourceSystem = source,
                    FileSize = fileSize,
                    Status = FileStatus.Loaded
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing row");
            }
        }

        return findings;
    }

    // 🔹 CSV Parsing
    private List<FileFinding> ParseCsv(Stream stream)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        var records = csv.GetRecords<dynamic>().ToList();

        var findings = new List<FileFinding>();

        foreach (var r in records)
        {
            findings.Add(new FileFinding
            {
                Id = Guid.NewGuid(),
                FileName = r.FileName,
                FilePath = r.FilePath,
                LastModifiedDate = DateTime.Parse(r.LastModifiedDate),
                SourceSystem = r.SourceSystem,
                FileSize = long.Parse(r.FileSize),
                Status = FileStatus.Loaded
            });
        }

        return findings;
    }
}
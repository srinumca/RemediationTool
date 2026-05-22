using ClosedXML.Excel;
using CsvHelper;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;
using System.Globalization;

namespace RemediationTool.Application.Services;

public class IngestionService
{
    private readonly ILogger<IngestionService> _logger;
    private readonly IFileFindingRepository _repository;
    private readonly IStorageService _storage;
    private readonly IValidator<FileFinding> _validator;
    private const int BatchSize = 1000;

    public IngestionService(        ILogger<IngestionService> logger, IFileFindingRepository repository, IStorageService storage,
        IValidator<FileFinding> validator)
    {
        _logger = logger;
        _repository = repository;
        _storage = storage;
        _validator = validator;
    }

    public async Task<int> ProcessAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new Exception("Invalid file");

        var ingestionId = Guid.NewGuid().ToString();
        var uploadedBy = "system";
        var loadTime = DateTime.UtcNow;

        var key = $"input/{file.FileName}";
        await _storage.UploadAsync(key, file.OpenReadStream());

        List<FileFinding> findings;

        var ext = Path.GetExtension(file.FileName).ToLower();
        using var stream = file.OpenReadStream();

        if (ext == ".xlsx")
            findings = ParseExcel(stream, ingestionId, file.FileName, uploadedBy, loadTime);
        else if (ext == ".csv")
            findings = ParseCsv(stream, ingestionId, file.FileName, uploadedBy, loadTime);
        else
            throw new Exception("Unsupported file format");

        _repository.AddRange(findings);

        return findings.Count;
    }

    // EXCEL PARSING
    private List<FileFinding> ParseExcel(
        Stream stream,
        string ingestionId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime)
    {
        var findings = new List<FileFinding>();

        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);

        // 🔥 Schema Validation
        var headers = sheet.Row(1).Cells().Select(c => c.GetString()).ToList();
        ValidateHeaders(headers);

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var finding = new FileFinding
            {
                Id = Guid.NewGuid(),
                FileName = row.Cell(1).GetString(),
                FilePath = row.Cell(2).GetString(),
                SourceSystem = row.Cell(4).GetString(),

                LastModifiedDate = DateTime.TryParse(row.Cell(3).GetString(), out var date)
                    ? date : default,

                FileSize = long.TryParse(row.Cell(5).GetString(), out var size)
                    ? size : 0,

                Status = FileStatus.Loaded,

                IngestionId = ingestionId,
                InboundFileName = inboundFileName,
                UploadedBy = uploadedBy,
                LoadDate = loadTime,
                UpdatedDate = loadTime
            };

            ApplyValidation(finding);
            findings.Add(finding);
        }

        return findings;
    }

    // CSV PARSING
    private List<FileFinding> ParseCsv(
        Stream stream,
        string ingestionId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime)
    {
        var findings = new List<FileFinding>();

        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        //Schema Validation
        csv.Read();
        csv.ReadHeader();
        ValidateHeaders(csv.HeaderRecord.ToList());

        var records = csv.GetRecords<dynamic>();

        foreach (var r in records)
        {
            var finding = new FileFinding
            {
                Id = Guid.NewGuid(),
                FileName = r.FileName,
                FilePath = r.FilePath,
                SourceSystem = r.SourceSystem,

                LastModifiedDate = DateTime.TryParse(r.LastModifiedDate, out var date)
                    ? date : default,

                FileSize = long.TryParse(r.FileSize, out var size)
                    ? size : 0,

                Status = FileStatus.Loaded,

                IngestionId = ingestionId,
                InboundFileName = inboundFileName,
                UploadedBy = uploadedBy,
                LoadDate = loadTime,
                UpdatedDate = loadTime
            };

            ApplyValidation(finding);
            findings.Add(finding);
        }

        return findings;
    }

    // COMMON VALIDATION HANDLER
    private void ApplyValidation(FileFinding finding)
    {
        var result = _validator.Validate(finding);

        if (!result.IsValid)
        {
            finding.IsValid = false;
            finding.Status = FileStatus.Failed;
            finding.ErrorReason = string.Join(", ", result.Errors.Select(e => e.ErrorMessage));
        }
        else
        {
            finding.IsValid = true;
        }
    }

    // SCHEMA VALIDATION
    private void ValidateHeaders(List<string> headers)
    {
        var requiredHeaders = new List<string>
        {
            "FileName",
            "FilePath",
            "LastModifiedDate",
            "SourceSystem",
            "FileSize"
        };

        var missing = requiredHeaders
            .Where(h => !headers.Any(x => x.Equals(h, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missing.Any())
            throw new Exception($"Missing required columns: {string.Join(", ", missing)}");
    }
}
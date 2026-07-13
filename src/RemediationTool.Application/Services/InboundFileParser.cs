using ClosedXML.Excel;
using CsvHelper;
using FluentValidation;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Models;
using RemediationTool.Application.Options;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemediationTool.Application.Services;

/// <summary>
/// Parses inbound CSV/XLSX files into validated findings.
/// Header positions are resolved once per file and reused for every row.
/// Invalid findings are converted directly to rejected-row details so only
/// valid findings are retained for staging, Parquet creation and persistence.
/// </summary>
internal sealed class InboundFileParser
{
    private readonly IValidator<FileFinding> _validator;
    private readonly ILogger _logger;
    private readonly IngestionProcessingOptions _options;

    public InboundFileParser(
        IValidator<FileFinding> validator,
        ILogger logger,
        IngestionProcessingOptions options)
    {
        _validator = validator;
        _logger = logger;
        _options = options;
    }

    public InboundParseResult Parse(
        Stream stream,
        string extension,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        return extension.ToLowerInvariant() switch
        {
            ".xlsx" => ParseExcel(stream, jobId, inboundFileName, uploadedBy, loadTime, cancellationToken),
            ".csv" => ParseCsv(stream, jobId, inboundFileName, uploadedBy, loadTime, cancellationToken),
            _ => throw new InvalidDataException("Unsupported file format. Only .csv and .xlsx files are allowed.")
        };
    }

    private InboundParseResult ParseExcel(
        Stream stream,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        var headerRow = sheet.FirstRowUsed()
            ?? throw new InvalidDataException("Excel file does not contain a header row.");

        var columnIndexes = BuildExcelColumnIndexMap(headerRow);
        var lastRowNumber = sheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
        var estimatedRecordCount = Math.Max(0, lastRowNumber - headerRow.RowNumber());
        var result = new InboundParseResult(estimatedRecordCount);

        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowNumber = row.RowNumber();
            var finding = MapExcelRowToFinding(
                row,
                columnIndexes,
                jobId,
                inboundFileName,
                uploadedBy,
                loadTime);

            if (IsBlankFinding(finding))
            {
                _logger.LogDebug(
                    "[BLANK_EXCEL_ROW_SKIPPED] JobId:{JobId}, RowNumber:{RowNumber}",
                    jobId,
                    rowNumber);
                continue;
            }

            result.RegisterSourceSystem(finding.OriginatingDataSystem);
            ValidateAndCollect(finding, rowNumber, result);
        }

        return result;
    }

    private InboundParseResult ParseCsv(
        Stream stream,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new InboundParseResult();
        var bufferSize = Math.Max(4096, _options.CsvReaderBufferSize);

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: bufferSize,
            leaveOpen: true);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        Dictionary<string, int> columnIndexes;
        try
        {
            if (!csv.Read())
                throw new InvalidDataException("CSV file does not contain a header row.");

            csv.ReadHeader();
            columnIndexes = BuildCsvColumnIndexMap(csv.HeaderRecord ?? Array.Empty<string>());
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"CSV header validation failed. {ex.Message}", ex);
        }

        while (TryReadNextCsvRow(csv, jobId, uploadedBy, result, out var rowNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var finding = MapCsvRowToFinding(
                    csv,
                    columnIndexes,
                    jobId,
                    inboundFileName,
                    uploadedBy,
                    loadTime);

                if (IsBlankFinding(finding))
                {
                    _logger.LogDebug(
                        "[BLANK_CSV_ROW_SKIPPED] JobId:{JobId}, RowNumber:{RowNumber}",
                        jobId,
                        rowNumber);
                    continue;
                }

                result.RegisterSourceSystem(finding.OriginatingDataSystem);
                ValidateAndCollect(finding, rowNumber, result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AddMalformedCsvRejectedRow(jobId, uploadedBy, rowNumber, ex, result);
            }
        }

        return result;
    }

    private bool TryReadNextCsvRow(
        CsvReader csv,
        string jobId,
        string uploadedBy,
        InboundParseResult result,
        out int rowNumber)
    {
        rowNumber = csv.Context.Parser.Row;

        try
        {
            if (!csv.Read())
                return false;

            rowNumber = csv.Context.Parser.Row;
            return true;
        }
        catch (Exception ex)
        {
            rowNumber = csv.Context.Parser.Row;
            AddMalformedCsvRejectedRow(jobId, uploadedBy, rowNumber, ex, result);

            // CsvHelper may not advance beyond a malformed physical row. Stop here
            // rather than retrying the same row indefinitely.
            return false;
        }
    }

    private void AddMalformedCsvRejectedRow(
        string jobId,
        string uploadedBy,
        int rowNumber,
        Exception ex,
        InboundParseResult result)
    {
        _logger.LogWarning(
            ex,
            "[MALFORMED_CSV_ROW] JobId:{JobId}, RowNumber:{RowNumber}",
            jobId,
            rowNumber);

        result.RegisterRejectedRecord();
        result.RejectedRows.Add(new RejectedRowSummary
        {
            RejectedRowId = Guid.NewGuid().ToString("N"),
            UserName = uploadedBy,
            RowNumber = rowNumber,
            FieldName = "CSV_ROW",
            ErrorReason = $"Malformed CSV row. {ex.Message}",
            ErrorCategory = ErrorCategory.Others.ToString(),
            ErrorDateUtc = DateTime.UtcNow
        });
    }

    private void ValidateAndCollect(
        FileFinding finding,
        int rowNumber,
        InboundParseResult result)
    {
        var validationResult = _validator.Validate(finding);

        if (validationResult.IsValid)
        {
            finding.IsValid = true;
            finding.IngestionErrorReason = string.Empty;
            result.AddValidFinding(finding);
            return;
        }

        finding.IsValid = false;
        finding.IngestionErrorReason = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
        result.RegisterRejectedRecord();

        var rawRowJson = SerializeFindingAsRawRow(finding);
        foreach (var error in validationResult.Errors)
        {
            result.RejectedRows.Add(new RejectedRowSummary
            {
                RejectedRowId = Guid.NewGuid().ToString("N"),
                SourceRecordId = finding.SourceRecordId,
                FindingFileName = finding.FindingFileName,
                FindingType = finding.FindingType,
                UserName = finding.UserName,
                RowNumber = rowNumber,
                FieldName = error.PropertyName,
                RejectedValue = GetRejectedValue(finding, error.PropertyName),
                ErrorReason = error.ErrorMessage,
                ErrorCategory = ErrorCategory.Others.ToString(),
                ErrorDateUtc = DateTime.UtcNow,
                RawRowJson = rawRowJson
            });
        }
    }

    private Dictionary<string, int> BuildExcelColumnIndexMap(IXLRow headerRow)
    {
        var headerCells = headerRow.CellsUsed().ToList();
        var headers = headerCells.Select(cell => cell.GetString()).ToList();
        ValidateHeaders(headers);

        var normalizedHeaderIndexes = headerCells
            .GroupBy(cell => NormalizeHeader(cell.GetString()), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Address.ColumnNumber,
                StringComparer.OrdinalIgnoreCase);

        return BuildKnownColumnIndexMap(normalizedHeaderIndexes);
    }

    private Dictionary<string, int> BuildCsvColumnIndexMap(IReadOnlyList<string> headers)
    {
        ValidateHeaders(headers);

        var normalizedHeaderIndexes = headers
            .Select((header, index) => new { Header = NormalizeHeader(header), Index = index })
            .GroupBy(item => item.Header, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Index,
                StringComparer.OrdinalIgnoreCase);

        return BuildKnownColumnIndexMap(normalizedHeaderIndexes);
    }

    private static Dictionary<string, int> BuildKnownColumnIndexMap(
        IReadOnlyDictionary<string, int> normalizedHeaderIndexes)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var columnName in InboundLayoutColumns.AllKnownColumns)
        {
            if (normalizedHeaderIndexes.TryGetValue(NormalizeHeader(columnName), out var index))
                result[columnName] = index;
        }

        return result;
    }

    private static void ValidateHeaders(IEnumerable<string?> headers)
    {
        var headerList = headers
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .Select(header => header!.Trim())
            .ToList();

        if (headerList.Count == 0)
            throw new InvalidDataException("Uploaded file does not contain any headers.");

        var duplicateHeaders = headerList
            .GroupBy(NormalizeHeader, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.First())
            .ToList();

        if (duplicateHeaders.Count > 0)
        {
            throw new InvalidDataException(
                $"Uploaded file contains duplicate headers: {string.Join(", ", duplicateHeaders)}");
        }

        var normalizedHeaders = headerList
            .Select(NormalizeHeader)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingRequired = InboundLayoutColumns.RequiredColumns
            .Where(column => !normalizedHeaders.Contains(NormalizeHeader(column)))
            .ToList();

        if (missingRequired.Count > 0)
        {
            throw new InvalidDataException(
                $"Uploaded file is missing required columns: {string.Join(", ", missingRequired)}");
        }
    }

    private static FileFinding MapExcelRowToFinding(
        IXLRow row,
        IReadOnlyDictionary<string, int> columnIndexes,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime)
    {
        return CreateFinding(
            columnName => GetExcelValue(row, columnIndexes, columnName),
            jobId,
            inboundFileName,
            uploadedBy,
            loadTime);
    }

    private static FileFinding MapCsvRowToFinding(
        CsvReader csv,
        IReadOnlyDictionary<string, int> columnIndexes,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime)
    {
        return CreateFinding(
            columnName => GetCsvValue(csv, columnIndexes, columnName),
            jobId,
            inboundFileName,
            uploadedBy,
            loadTime);
    }

    private static FileFinding CreateFinding(
        Func<string, string> getValue,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime)
    {
        return new FileFinding
        {
            Id = Guid.NewGuid(),
            RecordVersionId = Guid.NewGuid().ToString("N"),
            SourceRecordId = NullIfWhiteSpace(getValue(InboundLayoutColumns.SourceRecordId)),
            FindingFileName = getValue(InboundLayoutColumns.FindingFileName),
            FindingFileFormat = getValue(InboundLayoutColumns.FindingFileFormat),
            FindingFileSizeBytes = TryParseNullableLong(getValue(InboundLayoutColumns.FindingFileSize)),
            CurrentFileLocation = getValue(InboundLayoutColumns.CurrentFileLocation),
            FindingType = ParseFindingType(getValue(InboundLayoutColumns.FindingType)),
            DataSystem = getValue(InboundLayoutColumns.DataSystem),
            OriginatingDataSystem = getValue(InboundLayoutColumns.OriginatingDataSystem),
            OriginatingVendorTool = getValue(InboundLayoutColumns.OriginatingVendorTool),
            LastModifiedDateUtc = TryParseNullableDate(getValue(InboundLayoutColumns.LastModifiedDate)),
            CreatedDateUtc = TryParseNullableDate(getValue(InboundLayoutColumns.CreatedDate)),
            LastAccessedDateUtc = TryParseNullableDate(getValue(InboundLayoutColumns.LastAccessedDate)),
            SiteOwner = NullIfWhiteSpace(getValue(InboundLayoutColumns.SiteOwner)),
            FileOwner = NullIfWhiteSpace(getValue(InboundLayoutColumns.FileOwner)),
            BusinessUnit = NullIfWhiteSpace(getValue(InboundLayoutColumns.BusinessUnit)),
            Division = NullIfWhiteSpace(getValue(InboundLayoutColumns.Division)),
            Department = NullIfWhiteSpace(getValue(InboundLayoutColumns.Department)),
            Region = NullIfWhiteSpace(getValue(InboundLayoutColumns.Region)),
            Country = NullIfWhiteSpace(getValue(InboundLayoutColumns.Country)),
            PolicyName = NullIfWhiteSpace(getValue(InboundLayoutColumns.PolicyName)),
            PolicyId = NullIfWhiteSpace(getValue(InboundLayoutColumns.PolicyId)),
            FindingReason = NullIfWhiteSpace(getValue(InboundLayoutColumns.FindingReason)),
            RiskLevel = NullIfWhiteSpace(getValue(InboundLayoutColumns.RiskLevel)),
            SensitivityLabel = NullIfWhiteSpace(getValue(InboundLayoutColumns.SensitivityLabel)),
            DetectionDateUtc = TryParseNullableDate(getValue(InboundLayoutColumns.DetectionDate)),
            RecommendedAction = NullIfWhiteSpace(getValue(InboundLayoutColumns.RecommendedAction)),
            OriginalFileLocation = NullIfWhiteSpace(getValue(InboundLayoutColumns.OriginalFileLocation)),
            QuarantineDateUtc = TryParseNullableDate(getValue(InboundLayoutColumns.QuarantineDate)),
            RestorationTicketIdentifier = NullIfWhiteSpace(getValue(InboundLayoutColumns.RestorationTicketIdentifier)),
            RestorationRequestorEmail = NullIfWhiteSpace(getValue(InboundLayoutColumns.RestorationRequestorEmail)),
            RestorationComment = NullIfWhiteSpace(getValue(InboundLayoutColumns.RestorationComment)),
            IngestionJobId = jobId,
            InboundFileName = inboundFileName,
            UserName = uploadedBy,
            LoadDateUtc = loadTime,
            LastUpdateDateUtc = loadTime
        };
    }

    private static string GetExcelValue(
        IXLRow row,
        IReadOnlyDictionary<string, int> columnIndexes,
        string columnName)
    {
        return columnIndexes.TryGetValue(columnName, out var columnIndex)
            ? row.Cell(columnIndex).GetString().Trim()
            : string.Empty;
    }

    private static string GetCsvValue(
        CsvReader csv,
        IReadOnlyDictionary<string, int> columnIndexes,
        string columnName)
    {
        if (!columnIndexes.TryGetValue(columnName, out var columnIndex))
            return string.Empty;

        return csv.GetField(columnIndex)?.Trim() ?? string.Empty;
    }

    private static bool IsBlankFinding(FileFinding finding)
    {
        return string.IsNullOrWhiteSpace(finding.SourceRecordId)
            && string.IsNullOrWhiteSpace(finding.FindingFileName)
            && string.IsNullOrWhiteSpace(finding.FindingFileFormat)
            && !finding.FindingFileSizeBytes.HasValue
            && string.IsNullOrWhiteSpace(finding.CurrentFileLocation)
            && string.IsNullOrWhiteSpace(finding.FindingType)
            && string.IsNullOrWhiteSpace(finding.OriginatingDataSystem)
            && string.IsNullOrWhiteSpace(finding.OriginatingVendorTool)
            && string.IsNullOrWhiteSpace(finding.OriginalFileLocation)
            && !finding.QuarantineDateUtc.HasValue
            && string.IsNullOrWhiteSpace(finding.SiteOwner)
            && string.IsNullOrWhiteSpace(finding.FileOwner);
    }

    private static string SerializeFindingAsRawRow(FileFinding finding)
    {
        return JsonSerializer.Serialize(new
        {
            finding.SourceRecordId,
            finding.FindingFileName,
            finding.FindingFileFormat,
            finding.FindingFileSizeBytes,
            finding.CurrentFileLocation,
            finding.FindingType,
            finding.DataSystem,
            finding.OriginatingDataSystem,
            finding.OriginatingVendorTool,
            finding.OriginalFileLocation,
            finding.QuarantineDateUtc,
            finding.LastModifiedDateUtc,
            finding.CreatedDateUtc,
            finding.LastAccessedDateUtc,
            finding.SiteOwner,
            finding.FileOwner,
            finding.BusinessUnit,
            finding.Division,
            finding.Department,
            finding.Region,
            finding.Country,
            finding.PolicyName,
            finding.PolicyId,
            finding.FindingReason,
            finding.RiskLevel,
            finding.SensitivityLabel,
            finding.DetectionDateUtc,
            finding.RecommendedAction,
            finding.RestorationTicketIdentifier,
            finding.RestorationRequestorEmail,
            finding.RestorationComment
        }, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string? GetRejectedValue(FileFinding finding, string propertyName)
    {
        return propertyName switch
        {
            nameof(FileFinding.SourceRecordId) => finding.SourceRecordId,
            nameof(FileFinding.FindingFileName) => finding.FindingFileName,
            nameof(FileFinding.FindingFileFormat) => finding.FindingFileFormat,
            nameof(FileFinding.FindingFileSizeBytes) => finding.FindingFileSizeBytes?.ToString(CultureInfo.InvariantCulture),
            nameof(FileFinding.CurrentFileLocation) => finding.CurrentFileLocation,
            nameof(FileFinding.FindingType) => finding.FindingType,
            nameof(FileFinding.DataSystem) => finding.DataSystem,
            nameof(FileFinding.OriginatingDataSystem) => finding.OriginatingDataSystem,
            nameof(FileFinding.OriginatingVendorTool) => finding.OriginatingVendorTool,
            nameof(FileFinding.LastModifiedDateUtc) => finding.LastModifiedDateUtc?.ToString("O"),
            nameof(FileFinding.CreatedDateUtc) => finding.CreatedDateUtc?.ToString("O"),
            nameof(FileFinding.LastAccessedDateUtc) => finding.LastAccessedDateUtc?.ToString("O"),
            nameof(FileFinding.OriginalFileLocation) => finding.OriginalFileLocation,
            nameof(FileFinding.QuarantineDateUtc) => finding.QuarantineDateUtc?.ToString("O"),
            nameof(FileFinding.ExceptionDateUtc) => finding.ExceptionDateUtc?.ToString("O"),
            nameof(FileFinding.RestorationTicketIdentifier) => finding.RestorationTicketIdentifier,
            nameof(FileFinding.RestorationRequestorEmail) => finding.RestorationRequestorEmail,
            nameof(FileFinding.RestorationComment) => finding.RestorationComment,
            _ => null
        };
    }

    private static DateTime? TryParseNullableDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private static long? TryParseNullableLong(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Replace(",", string.Empty).Trim();
        if (long.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
            return parsedLong;

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDecimal)
            ? Convert.ToInt64(parsedDecimal)
            : null;
    }

    private static string ParseFindingType(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string? NullIfWhiteSpace(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }
}

internal sealed class InboundParseResult
{
    private readonly HashSet<string> _sourceSystems = new(StringComparer.OrdinalIgnoreCase);

    public InboundParseResult(int estimatedRecordCount = 0)
    {
        ValidFindings = estimatedRecordCount > 0
            ? new List<FileFinding>(estimatedRecordCount)
            : new List<FileFinding>();
    }

    public List<FileFinding> ValidFindings { get; }

    public List<RejectedRowSummary> RejectedRows { get; } = new();

    public Dictionary<string, int> FindingTypeCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int RejectedRecordCount { get; private set; }

    public int SuccessCount => ValidFindings.Count;

    public int TotalRecords => SuccessCount + RejectedRecordCount;

    public string SourceSystem => _sourceSystems.Count switch
    {
        0 => "Unknown",
        1 => _sourceSystems.First(),
        _ => "Multiple"
    };

    public void AddValidFinding(FileFinding finding)
    {
        ValidFindings.Add(finding);

        var findingType = finding.FindingType ?? string.Empty;
        if (FindingTypeCounts.TryGetValue(findingType, out var existingCount))
            FindingTypeCounts[findingType] = existingCount + 1;
        else
            FindingTypeCounts[findingType] = 1;
    }

    public void RegisterRejectedRecord()
        => RejectedRecordCount++;

    public void RegisterSourceSystem(string? sourceSystem)
    {
        if (!string.IsNullOrWhiteSpace(sourceSystem))
            _sourceSystems.Add(sourceSystem.Trim());
    }
}

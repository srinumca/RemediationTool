using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using RemediationTool.Application.Options;
using RemediationTool.Application.Services;
using RemediationTool.Application.Validators;
using Xunit;

namespace RemediationTool.Application.Tests;

public sealed class InboundFileParserAdvancedTests
{
    private static readonly DateTime LoadTime =
        new(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Parse_UnsupportedExtension_ThrowsClearError()
    {
        var parser = CreateParser();
        using var stream = CsvStream(MinimumHeader);

        var exception = Assert.Throws<InvalidDataException>(() =>
            parser.Parse(stream, ".json", "job-1", "report.json", "user", LoadTime));

        Assert.Contains("Only .csv and .xlsx", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_PreCancelledToken_ThrowsBeforeReading()
    {
        var parser = CreateParser();
        using var stream = CsvStream(MinimumHeader);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            parser.Parse(
                stream,
                ".csv",
                "job-1",
                "report.csv",
                "user",
                LoadTime,
                cancellation.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_CsvWithoutHeader_ThrowsHeaderValidationError(string content)
    {
        var parser = CreateParser();
        using var stream = CsvStream(content);

        var exception = Assert.Throws<InvalidDataException>(() =>
            parser.Parse(stream, ".csv", "job-1", "report.csv", "user", LoadTime));

        Assert.Contains("header", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_CsvMissingRequiredColumns_ListsMissingColumns()
    {
        var parser = CreateParser();
        using var stream = CsvStream("Finding_File_Name,Finding_Type\nfile.txt,Obsolete");

        var exception = Assert.Throws<InvalidDataException>(() =>
            parser.Parse(stream, ".csv", "job-1", "report.csv", "user", LoadTime));

        Assert.Contains("missing required columns", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Finding File Format", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Current_File_Location", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CsvDuplicateNormalizedHeaders_IsRejected()
    {
        var parser = CreateParser();
        var csv =
            "Finding_File_Name,Finding File Name,Finding File Format,Current_File_Location,Finding_Type,Originating_Data_System,Originating_Vendor_Tool\n"
            + "file.txt,file.txt,txt,/source/file.txt,Obsolete,SMB,EDG";
        using var stream = CsvStream(csv);

        var exception = Assert.Throws<InvalidDataException>(() =>
            parser.Parse(stream, ".csv", "job-1", "report.csv", "user", LoadTime));

        Assert.Contains("duplicate headers", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_CsvHeaderNormalizationAndOptionalFields_MapCorrectly()
    {
        var parser = CreateParser();
        var csv =
            "finding file name,FINDING-FILE-FORMAT,current file location,finding type,originating data system,originating vendor tool,ID,Finding_File_Size,Data_System,Last_Modified_Date,Risk_Level\n"
            + "source.txt,txt,/source/source.txt,Obsolete,SMB,EDG,record-1,2048,NetApp,2026-01-02T03:04:05Z,High";
        using var stream = CsvStream(csv);

        var result = parser.Parse(
            stream,
            ".csv",
            "job-1",
            "report.csv",
            "user-1",
            LoadTime);

        var finding = Assert.Single(result.ValidFindings);
        Assert.Equal(1, result.TotalRecords);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.RejectedRecordCount);
        Assert.Equal("SMB", result.SourceSystem);
        Assert.Equal("record-1", finding.SourceRecordId);
        Assert.Equal("source.txt", finding.FindingFileName);
        Assert.Equal(2048, finding.FindingFileSizeBytes);
        Assert.Equal("NetApp", finding.SourceSystemPlatform);
        Assert.Equal("job-1", finding.IngestionJobId);
        Assert.Equal("report.csv", finding.InboundFileName);
        Assert.Equal("user-1", finding.UserName);
        Assert.Equal(LoadTime, finding.LoadDateUtc);
        Assert.Equal("High", finding.RiskLevel);
        Assert.Equal(1, result.FindingTypeCounts["Obsolete"]);
    }

    [Fact]
    public void Parse_MixedCsv_IsolatesInvalidRowWithoutBlockingValidRow()
    {
        var parser = CreateParser();
        var csv = MinimumHeader + "\n"
            + "valid.txt,txt,/source/valid.txt,Obsolete,SMB,EDG\n"
            + "invalid.txt,txt,/source/invalid.txt,Unsupported,SMB,EDG";
        using var stream = CsvStream(csv);

        var result = parser.Parse(
            stream,
            ".csv",
            "job-1",
            "report.csv",
            "user",
            LoadTime);

        Assert.Equal(2, result.TotalRecords);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.RejectedRecordCount);
        Assert.Single(result.ValidFindings);
        Assert.Contains(
            result.RejectedRows,
            row => row.FieldName == "FindingType"
                && row.RowNumber == 3
                && row.RawRowJson is not null);
    }

    [Fact]
    public void Parse_BlankCsvRows_AreSkippedAndNotCountedAsRejected()
    {
        var parser = CreateParser();
        var csv = MinimumHeader + "\n,,,,,\n"
            + "valid.txt,txt,/source/valid.txt,Obsolete,SMB,EDG\n,,,,,";
        using var stream = CsvStream(csv);

        var result = parser.Parse(
            stream,
            ".csv",
            "job-1",
            "report.csv",
            "user",
            LoadTime);

        Assert.Equal(1, result.TotalRecords);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.RejectedRecordCount);
    }

    [Fact]
    public void Parse_MultipleOriginatingSystems_ReportsMultiple()
    {
        var parser = CreateParser();
        var csv = MinimumHeader + "\n"
            + "one.txt,txt,/source/one.txt,Obsolete,SMB,EDG\n"
            + "two.txt,txt,/source/two.txt,Obsolete,SharePoint,EDG";
        using var stream = CsvStream(csv);

        var result = parser.Parse(
            stream,
            ".csv",
            "job-1",
            "report.csv",
            "user",
            LoadTime);

        Assert.Equal("Multiple", result.SourceSystem);
        Assert.Equal(2, result.SuccessCount);
    }

    [Fact]
    public void Parse_QuarantinedRowMissingWorkflowFields_IsRejectedOnce()
    {
        var parser = CreateParser();
        var csv = MinimumHeader + "\n"
            + "quarantined.txt,txt,/quarantine/file.txt,Quarantined,SMB,EDG";
        using var stream = CsvStream(csv);

        var result = parser.Parse(
            stream,
            ".csv",
            "job-1",
            "report.csv",
            "user",
            LoadTime);

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.RejectedRecordCount);
        Assert.Contains(result.RejectedRows, row => row.FieldName == "OriginalFileLocation");
        Assert.Contains(result.RejectedRows, row => row.FieldName == "QuarantineDateUtc");
    }

    [Fact]
    public void Parse_Excel_MapsValidRowsAndSkipsBlankRows()
    {
        var parser = CreateParser();
        using var stream = CreateExcel(
            new[]
            {
                MinimumHeaders,
                new[] { "excel.txt", "txt", "/source/excel.txt", "Obsolete", "SMB", "EDG" },
                new[] { "", "", "", "", "", "" }
            });

        var result = parser.Parse(
            stream,
            ".xlsx",
            "job-xlsx",
            "report.xlsx",
            "excel-user",
            LoadTime);

        var finding = Assert.Single(result.ValidFindings);
        Assert.Equal("excel.txt", finding.FindingFileName);
        Assert.Equal("job-xlsx", finding.IngestionJobId);
        Assert.Equal(1, result.TotalRecords);
        Assert.Equal("SMB", result.SourceSystem);
    }

    [Fact]
    public void Parse_ExcelWithoutHeader_ThrowsClearError()
    {
        var parser = CreateParser();
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Sheet1");
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var exception = Assert.Throws<InvalidDataException>(() =>
            parser.Parse(stream, ".xlsx", "job-1", "empty.xlsx", "user", LoadTime));

        Assert.Contains("header row", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InboundParseResult_AggregatesCountsCaseInsensitivelyAndTracksSourceSystems()
    {
        var result = new InboundParseResult();
        result.AddValidFinding(new RemediationTool.Domain.Entities.FileFinding { FindingType = "Obsolete" });
        result.AddValidFinding(new RemediationTool.Domain.Entities.FileFinding { FindingType = "obsolete" });
        result.RegisterRejectedRecord();
        result.RegisterSourceSystem(" SMB ");
        result.RegisterSourceSystem("smb");

        Assert.Equal(3, result.TotalRecords);
        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(1, result.RejectedRecordCount);
        Assert.Equal(2, result.FindingTypeCounts["OBSOLETE"]);
        Assert.Equal("SMB", result.SourceSystem);
    }

    private static InboundFileParser CreateParser()
        => new(
            new FileFindingValidator(),
            NullLogger.Instance,
            new IngestionProcessingOptions
            {
                CsvReaderBufferSize = 128
            });

    private static MemoryStream CsvStream(string content)
        => new(Encoding.UTF8.GetBytes(content));

    private static MemoryStream CreateExcel(IEnumerable<string[]> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Findings");
        var rowNumber = 1;

        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
                worksheet.Cell(rowNumber, column + 1).Value = row[column];

            rowNumber++;
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static readonly string[] MinimumHeaders =
    {
        "Finding_File_Name",
        "Finding File Format",
        "Current_File_Location",
        "Finding_Type",
        "Originating_Data_System",
        "Originating_Vendor_Tool"
    };

    private static string MinimumHeader => string.Join(',', MinimumHeaders);
}

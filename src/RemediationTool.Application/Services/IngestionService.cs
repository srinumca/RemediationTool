
using ClosedXML.Excel;
using CsvHelper;
using System.Globalization;
using Microsoft.Extensions.Logging;
using RemediationTool.Domain;

namespace RemediationTool.Application.Services;

// Handles CSV / Excel findings ingestion
public class IngestionService
{
    private readonly ILogger<IngestionService> _logger;
    private readonly IFileFindingRepository _repository;

    public IngestionService(ILogger<IngestionService> logger, IFileFindingRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public int ParseFile(Stream stream, string filename)
    {
        try
        {
            var ext = Path.GetExtension(filename).ToLower();
            List<FileFinding> findings;

            if(ext == ".xlsx")
                findings = ParseExcel(stream);
            else if(ext == ".csv")
                findings = ParseCsv(stream);
            else
                throw new Exception("Unsupported file format");

            _repository.AddRange(findings);
            return findings.Count;
        }
        catch(Exception ex)
        {
            _logger.LogError(ex,"Error during ingestion");
            throw;
        }
    }

    private List<FileFinding> ParseExcel(Stream stream)
    {
        try
        {
            var findings = new List<FileFinding>();
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet(1);

            foreach(var row in sheet.RowsUsed().Skip(1))
            {
                findings.Add(new FileFinding{
                    FileName = row.Cell(1).GetString(),
                    //FilePath = row.Cell(2).GetString(),
                    //SourceSystem = row.Cell(3).GetString(),
                    //FileSize = row.Cell(4).GetValue<long>(),
                    LastModifiedDate = row.Cell(5).GetDateTime()
                });
            }

            return findings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Excel file");
            throw;
        }
    }

    private List<FileFinding> ParseCsv(Stream stream)
    {
        try
        {
            using var reader = new StreamReader(stream);
            using var csv = new CsvReader(reader,CultureInfo.InvariantCulture);
            return csv.GetRecords<FileFinding>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing CSV file");
            throw;
        }
    }
}

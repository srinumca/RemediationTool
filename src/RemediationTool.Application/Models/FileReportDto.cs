namespace RemediationTool.Application.Models;

public class FileReportDto
{
    public string FileName { get; set; }
    public string Status { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public string? QuarantinePath { get; set; }
}
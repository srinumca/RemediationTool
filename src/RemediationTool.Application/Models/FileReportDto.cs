using RemediationTool.Domain;

namespace RemediationTool.Application.Models;

public class FileReportDto
{
    public string FileName { get; set; }
    public FileStatus Status { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public string? QuarantinePath { get; set; }
}
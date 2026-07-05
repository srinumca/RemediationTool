using RemediationTool.Domain;

namespace RemediationTool.Application.Models;

public sealed class QuarantineItemResult
{
    public Guid RecordId { get; set; }

    public string? SourceRecordId { get; set; }

    public string? FileName { get; set; }

    public FileStatus StartingStatus { get; set; }

    public FileStatus FinalStatus { get; set; }

    public bool Succeeded { get; set; }

    public bool Skipped { get; set; }

    public int AttemptCount { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? ErrorCategory { get; set; }

    public string? OriginalPath { get; set; }

    public string? QuarantinePath { get; set; }
}

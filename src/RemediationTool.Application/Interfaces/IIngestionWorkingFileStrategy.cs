using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Interfaces;

public interface IIngestionWorkingFileStrategy
{
    string Format { get; }

    Task<IngestionWorkingFileResult> WriteAsync(
        string jobId,
        string inboundFileName,
        IReadOnlyList<FileFinding> validFindings,
        CancellationToken cancellationToken = default);
}

public class IngestionWorkingFileResult
{
    public string Format { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public int RecordCount { get; set; }
}

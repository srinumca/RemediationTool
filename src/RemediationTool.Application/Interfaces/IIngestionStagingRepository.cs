using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Interfaces;

public interface IIngestionStagingRepository
{
    /// <summary>
    /// Saves valid findings for the current ingestion job.
    /// </summary>
    void SaveValidFindings(string jobId, List<FileFinding> validFindings);

    /// <summary>
    /// Removes staged records after successful processing.
    /// </summary>
    void DeleteByJobId(string jobId);
}

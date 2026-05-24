using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Interfaces;

public interface IIngestionStagingRepository
{
    void SaveValidFindings(string jobId, List<FileFinding> validFindings);

    List<FileFinding> GetValidFindingsAfter(string jobId, int lastProcessedRecordCount);

    int CountByJobId(string jobId);
}
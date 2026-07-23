using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Interfaces;

public interface IIngestionStagingRepository
{
    /// <summary>
    /// Saves all valid findings for a job to the staging store, keyed by JobId and SequenceNumber.
    /// Replaces any existing staged records for the same JobId (idempotent on re-upload).
    /// </summary>
    void SaveValidFindings(string jobId, List<FileFinding> validFindings);

    /// <summary>
    /// Returns all staged findings for the given JobId where SequenceNumber > lastProcessedRecordCount.
    /// Used by the resume flow to load only the unprocessed tail of records.
    /// Records are returned ordered by SequenceNumber ascending.
    /// </summary>
    List<FileFinding> GetValidFindingsAfter(string jobId, int lastProcessedRecordCount);

    /// <summary>
    /// Returns the total count of staged records for the given JobId.
    /// Used as a pre-flight check before attempting resume.
    /// </summary>
    int CountByJobId(string jobId);

    /// <summary>
    /// Removes all staged records for the given JobId.
    /// Called after a job completes successfully (Status = Success or PartialSuccess)
    /// to prevent unbounded growth of the staging store.
    /// Also called after a successful resume to clean up remaining staged records.
    /// </summary>
    void DeleteByJobId(string jobId);
}
using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Interfaces;

public interface IIngestionCheckpointRepository
{
    IngestionCheckpoint? GetByJobId(string jobId);

    void Upsert(IngestionCheckpoint checkpoint);
}
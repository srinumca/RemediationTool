using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Interfaces;

public interface IIngestionCheckpointRepository
{
    void Upsert(IngestionCheckpoint checkpoint);
}

using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

public interface IIngestionJobAuditRepository
{
    List<IngestionJobAudit> GetAll();

    IngestionJobAudit? GetByJobId(string jobId);

    void Add(IngestionJobAudit audit);

    void Update(IngestionJobAudit audit);
}
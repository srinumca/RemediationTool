using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class DynamoDbIngestionJobAuditRepository : IIngestionJobAuditRepository
{
    public List<IngestionJobAudit> GetAll()
    {
        throw new NotImplementedException(
            "DynamoDB ingestion job audit persistence is not implemented yet. Set Persistence:Provider to Json.");
    }

    public IngestionJobAudit? GetByJobId(string jobId)
    {
        throw new NotImplementedException(
            "DynamoDB ingestion job audit persistence is not implemented yet. Set Persistence:Provider to Json.");
    }

    public void Add(IngestionJobAudit audit)
    {
        throw new NotImplementedException(
            "DynamoDB ingestion job audit persistence is not implemented yet. Set Persistence:Provider to Json.");
    }

    public void Update(IngestionJobAudit audit)
    {
        throw new NotImplementedException(
            "DynamoDB ingestion job audit persistence is not implemented yet. Set Persistence:Provider to Json.");
    }
}
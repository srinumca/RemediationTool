using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class DynamoDbRejectedRowRepository : IRejectedRowRepository
{
    public List<RejectedRowDetail> GetAll()
    {
        throw new NotImplementedException(
            "DynamoDB rejected row persistence is not implemented yet. Set Persistence:Provider to Json.");
    }

    public List<RejectedRowDetail> GetByJobId(string jobId)
    {
        throw new NotImplementedException(
            "DynamoDB rejected row persistence is not implemented yet. Set Persistence:Provider to Json.");
    }

    public void AddRange(List<RejectedRowDetail> rejectedRows)
    {
        throw new NotImplementedException(
            "DynamoDB rejected row persistence is not implemented yet. Set Persistence:Provider to Json.");
    }
}
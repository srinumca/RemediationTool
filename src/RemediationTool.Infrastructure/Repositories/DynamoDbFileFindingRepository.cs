using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class DynamoDbFileFindingRepository : IFileFindingRepository
{
    public List<FileFinding> GetAll()
    {
        throw new NotImplementedException(
            "DynamoDB persistence is not implemented yet. Set Persistence:Provider to Json.");
    }

    public FileFinding? GetById(Guid id)
    {
        throw new NotImplementedException(
            "DynamoDB persistence is not implemented yet. Set Persistence:Provider to Json.");
    }

    public void Add(FileFinding finding)
    {
        throw new NotImplementedException(
            "DynamoDB persistence is not implemented yet. Set Persistence:Provider to Json.");
    }

    public void AddRange(List<FileFinding> findings)
    {
        throw new NotImplementedException(
            "DynamoDB persistence is not implemented yet. Set Persistence:Provider to Json.");
    }

    public void Update(FileFinding finding)
    {
        throw new NotImplementedException(
            "DynamoDB persistence is not implemented yet. Set Persistence:Provider to Json.");
    }
}
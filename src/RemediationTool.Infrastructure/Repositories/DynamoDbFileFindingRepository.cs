using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// DynamoDB implementation of IFileFindingRepository.
/// All methods throw NotImplementedException until AWS connectivity is established.
/// Switch by setting Persistence:Provider = DynamoDB in appsettings.json.
/// </summary>
public class DynamoDbFileFindingRepository : IFileFindingRepository
{
    private const string Msg =
        "DynamoDB persistence is not implemented yet. Set Persistence:Provider to Json.";

    public void AddRange(IReadOnlyList<FileFinding> findings) => throw new NotImplementedException(Msg);
    public void Add(FileFinding finding) => throw new NotImplementedException(Msg);
    public void Update(FileFinding finding) => throw new NotImplementedException(Msg);

    public FileFinding? GetById(Guid id) => throw new NotImplementedException(Msg);
    public FileFinding? GetLatestBySourceRecordId(string sourceRecordId) => throw new NotImplementedException(Msg);

    public IReadOnlyList<FileFinding> GetLatestByFindingType(FindingType findingType) => throw new NotImplementedException(Msg);
    public IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem) => throw new NotImplementedException(Msg);
    public IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId) => throw new NotImplementedException(Msg);
    public IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId) => throw new NotImplementedException(Msg);

    public PagedResult<FileFinding> GetLatestPaged(int pageSize, string? lastEvaluatedKey = null, FindingType? findingType = null)
        => throw new NotImplementedException(Msg);

    public IReadOnlyDictionary<FindingType, int> GetCountByFindingType() => throw new NotImplementedException(Msg);
    public int CountByFindingType(FindingType findingType) => throw new NotImplementedException(Msg);
}
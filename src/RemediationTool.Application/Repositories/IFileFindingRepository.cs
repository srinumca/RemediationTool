using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

public interface IFileFindingRepository
{
    List<FileFinding> GetAll();
    void AddRange(List<FileFinding> records);
    void Update(FileFinding record);
}
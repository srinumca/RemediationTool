using RemediationTool.Domain;

public interface IFileFindingRepository
{
    List<FileFinding> GetAll();
    void AddRange(List<FileFinding> records);
    void Update(FileFinding record);
}
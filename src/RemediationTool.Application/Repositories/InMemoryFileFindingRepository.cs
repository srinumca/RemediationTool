
using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

// Simple in-memory storage used for POC
public class InMemoryFileFindingRepository : IFileFindingRepository
{
    private static List<FileFinding> _store = new();

    public List<FileFinding> GetAll()
    {
        try
        {
            return _store;
        }
        catch
        {
            throw;
        }
    }

    public void AddRange(List<FileFinding> findings)
    {
        try
        {
            _store.AddRange(findings);
        }
        catch
        {
            throw;
        }
    }

    public void Update(FileFinding finding)
    {
        try
        {
            var index = _store.FindIndex(x => x.Id == finding.Id);
            if(index >= 0)
                _store[index] = finding;
        }
        catch
        {
            throw;
        }
    }
}

using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

public interface IRejectedRowRepository
{
    List<RejectedRowDetail> GetAll();

    List<RejectedRowDetail> GetByJobId(string jobId);

    void AddRange(List<RejectedRowDetail> rejectedRows);
}
namespace RemediationTool.Application.Models;

public sealed class DashboardSummaryDto
{
    public int Total { get; init; }

    public Dictionary<string, int> ByStatus { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> ByFindingType { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> ByTab { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public int PendingActionCount { get; init; }

    public int ErrorCount { get; init; }

    public int ExceptionCount { get; init; }

    public int QuarantinedCount { get; init; }

    public int RestorationCount { get; init; }

    public int DeletedCount { get; init; }
}

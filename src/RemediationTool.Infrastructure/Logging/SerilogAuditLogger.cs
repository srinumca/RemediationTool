// ─────────────────────────────────────────────────────────────────────────────
// FILE: src/RemediationTool.Infrastructure/Logging/SerilogAuditLogger.cs  (NEW FILE)
//
// Concrete implementation of IAuditLogger. Tags every event with
// LogCategory=Audit via a logging scope, which the Serilog sub-logger filter
// configured in Program.cs uses to route the event to logs/audit-*.log
// instead of the regular operational log file.
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.Logging;
using RemediationTool.Application.Logging;

namespace RemediationTool.Infrastructure.Logging;

public class SerilogAuditLogger : IAuditLogger
{
    private readonly ILogger<SerilogAuditLogger> _logger;

    public SerilogAuditLogger(ILogger<SerilogAuditLogger> logger)
    {
        _logger = logger;
    }

    public void RecordEvent(
        string eventType,
        string entityId,
        string actor,
        string outcome,
        object? details = null)
    {
        // LogCategory=Audit is the property Serilog's sub-logger filter
        // (configured in Program.cs) checks to route this line to
        // logs/audit-*.log INSTEAD OF the operational file.
        //
        // Using a scope here (not LogContext.PushProperty) keeps the
        // property attached to exactly this one log call and nothing else
        // written before/after it on the same async flow.
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["LogCategory"] = "Audit",
            ["AuditEventType"] = eventType,
            ["AuditEntityId"] = entityId,
            ["AuditActor"] = actor,
            ["AuditOutcome"] = outcome
        }))
        {
            _logger.LogInformation(
                "[AUDIT] {EventType} EntityId={EntityId} Actor={Actor} Outcome={Outcome} Details={@Details}",
                eventType, entityId, actor, outcome, details);
        }
    }
}
namespace AiOrchestrator.McpServers.ApprovalWorkflow;

/// <summary>Stand-in for a real internal "Approval Workflow" REST API.</summary>
public sealed record ApprovalRequest(
    string RequestId,
    string Type,
    string EntityId,
    string RequestedBy,
    decimal? Amount,
    string Status,
    string CurrentApprover,
    DateOnly SubmittedDate,
    int SlaHoursRemaining);

public sealed class MockApprovalWorkflowApiClient
{
    private readonly List<ApprovalRequest> _requests = new()
    {
        new("APR-5001", "PayeeOnboarding", "PAY-1003", "j.morgan@company.com", null, "Pending", "compliance-team", new DateOnly(2026, 8, 27), 6),
        new("APR-5002", "Payment", "PAY-1007", "s.reyes@company.com", 82000m, "Pending", "finance-director", new DateOnly(2026, 8, 29), 18),
        new("APR-5003", "LimitIncrease", "PAY-1008", "a.khan@company.com", 50000m, "Approved", "finance-director", new DateOnly(2026, 8, 20), 0),
        new("APR-5004", "PayeeOnboarding", "PAY-1005", "j.morgan@company.com", null, "Rejected", "compliance-team", new DateOnly(2025, 12, 11), 0),
        new("APR-5005", "Payment", "PAY-1002", "s.reyes@company.com", 15250m, "Approved", "finance-director", new DateOnly(2026, 7, 16), 0),
        new("APR-5006", "Payment", "PAY-1001", "s.reyes@company.com", 4200m, "Pending", "team-lead", new DateOnly(2026, 8, 30), 3),
    };

    public ApprovalRequest? GetById(string requestId) =>
        _requests.FirstOrDefault(r => string.Equals(r.RequestId, requestId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ApprovalRequest> ListPending(string? approver) =>
        _requests
            .Where(r => r.Status == "Pending" &&
                        (approver is null || string.Equals(r.CurrentApprover, approver, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    public IReadOnlyList<ApprovalRequest> HistoryForEntity(string entityId) =>
        _requests.Where(r => string.Equals(r.EntityId, entityId, StringComparison.OrdinalIgnoreCase)).ToList();
}

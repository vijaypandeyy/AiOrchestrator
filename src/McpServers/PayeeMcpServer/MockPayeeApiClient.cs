namespace AiOrchestrator.McpServers.Payee;

/// <summary>
/// Stand-in for a real internal "Payee Master Data" REST API. In a production deployment this
/// class would be replaced by an HttpClient-based client calling the real service (with auth,
/// retries, etc.) - everything above it (the MCP tool wiring in Program.cs) stays identical,
/// because the MCP tool boundary is exactly where that swap is meant to happen.
/// </summary>
public sealed record PayeeRecord(
    string PayeeId,
    string Name,
    string AccountNumberMasked,
    string BankName,
    string Country,
    string Status,
    DateOnly LastPaymentDate);

public sealed class MockPayeeApiClient
{
    private readonly List<PayeeRecord> _payees = new()
    {
        new("PAY-1001", "Acme Logistics Ltd", "****4821", "HSBC UK", "United Kingdom", "Active", new DateOnly(2026, 8, 12)),
        new("PAY-1002", "Nairobi Freight Partners", "****7790", "Equity Bank", "Kenya", "Active", new DateOnly(2026, 7, 29)),
        new("PAY-1003", "Sterling Office Supplies", "****3305", "Chase", "United States", "PendingVerification", new DateOnly(2026, 6, 2)),
        new("PAY-1004", "Meridian Consulting GmbH", "****9012", "Deutsche Bank", "Germany", "Active", new DateOnly(2026, 8, 20)),
        new("PAY-1005", "Blue Harbor Shipping Co", "****1187", "DBS Bank", "Singapore", "Inactive", new DateOnly(2025, 12, 15)),
        new("PAY-1006", "Sterling Office Supplies EU", "****3306", "ING", "Netherlands", "Active", new DateOnly(2026, 8, 1)),
        new("PAY-1007", "Quantum Retail Holdings", "****5540", "Barclays", "United Kingdom", "Active", new DateOnly(2026, 8, 25)),
        new("PAY-1008", "Delta Facilities Management", "****2298", "Wells Fargo", "United States", "Active", new DateOnly(2026, 8, 28)),
    };

    public PayeeRecord? GetById(string payeeId) =>
        _payees.FirstOrDefault(p => string.Equals(p.PayeeId, payeeId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<PayeeRecord> SearchByName(string nameFragment) =>
        _payees.Where(p => p.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<PayeeRecord> ListRecent(int limit) =>
        _payees.OrderByDescending(p => p.LastPaymentDate).Take(Math.Max(1, limit)).ToList();
}

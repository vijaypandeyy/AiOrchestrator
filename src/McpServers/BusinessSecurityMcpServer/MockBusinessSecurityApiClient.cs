namespace AiOrchestrator.McpServers.BusinessSecurity;

/// <summary>Stand-in for a real internal "Business Security / Fraud & KYC" REST API.</summary>
public sealed record SecurityProfile(
    string EntityId,
    int RiskScore,
    string RiskCategory,
    string KycStatus,
    bool SanctionsListMatch,
    List<string> FraudFlags,
    DateOnly LastReviewedDate);

public sealed class MockBusinessSecurityApiClient
{
    private readonly List<SecurityProfile> _profiles = new()
    {
        new("PAY-1001", 12, "Low", "Verified", false, new(), new DateOnly(2026, 8, 1)),
        new("PAY-1002", 34, "Medium", "Verified", false,
            new() { "Unusual payment velocity in last 30 days" }, new DateOnly(2026, 7, 15)),
        new("PAY-1003", 71, "High", "Pending", false,
            new() { "KYC documentation incomplete", "New payee flagged for manual review" }, new DateOnly(2026, 6, 3)),
        new("PAY-1004", 8, "Low", "Verified", false, new(), new DateOnly(2026, 8, 18)),
        new("PAY-1005", 88, "High", "Failed", true,
            new() { "Sanctions list partial name match", "Account marked inactive after failed KYC refresh" }, new DateOnly(2025, 12, 10)),
        new("PAY-1006", 22, "Low", "Verified", false, new(), new DateOnly(2026, 7, 30)),
        new("PAY-1007", 15, "Low", "Verified", false, new(), new DateOnly(2026, 8, 22)),
        new("PAY-1008", 45, "Medium", "Verified", false,
            new() { "First payment above $50,000 threshold" }, new DateOnly(2026, 8, 20)),
    };

    public SecurityProfile? GetProfile(string entityId) =>
        _profiles.FirstOrDefault(p => string.Equals(p.EntityId, entityId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<SecurityProfile> ListHighRisk() =>
        _profiles.Where(p => p.RiskCategory == "High").ToList();
}

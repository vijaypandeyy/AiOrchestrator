using System.Text.Json;
using AiOrchestrator.McpServers.BusinessSecurity;
using AiOrchestrator.McpServers.Common;

const string ServerName = "business-security-mcp-server";
var api = new MockBusinessSecurityApiClient();
var jsonOptions = new JsonSerializerOptions { WriteIndented = false };

var host = new McpServerHost(ServerName)
    .AddTool(
        name: "get_security_profile",
        description: "Get the full business-security/fraud/KYC profile for a payee or business entity: " +
                      "risk score, risk category, KYC status, sanctions-list match, and active fraud flags.",
        inputSchema: Schema.Parse("""
            {
              "type": "object",
              "properties": {
                "entityId": { "type": "string", "description": "Payee or business entity ID, e.g. PAY-1001" }
              },
              "required": ["entityId"]
            }
            """),
        handler: (args, _) =>
        {
            var entityId = args.TryGetProperty("entityId", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return Task.FromResult(McpToolInvocationOutcome.Failure("entityId is required."));
            }

            var profile = api.GetProfile(entityId);
            var outcome = profile is null
                ? McpToolInvocationOutcome.Failure($"No security profile found for entity '{entityId}'.")
                : McpToolInvocationOutcome.Ok(JsonSerializer.Serialize(profile, jsonOptions));
            return Task.FromResult(outcome);
        })
    .AddTool(
        name: "check_fraud_flags",
        description: "Return only the active fraud flags for a payee/entity (a lighter-weight " +
                      "check than get_security_profile when the user only cares about flags).",
        inputSchema: Schema.Parse("""
            {
              "type": "object",
              "properties": {
                "entityId": { "type": "string", "description": "Payee or business entity ID" }
              },
              "required": ["entityId"]
            }
            """),
        handler: (args, _) =>
        {
            var entityId = args.TryGetProperty("entityId", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return Task.FromResult(McpToolInvocationOutcome.Failure("entityId is required."));
            }

            var profile = api.GetProfile(entityId);
            if (profile is null)
            {
                return Task.FromResult(McpToolInvocationOutcome.Failure($"No security profile found for entity '{entityId}'."));
            }

            var outcome = profile.FraudFlags.Count == 0
                ? McpToolInvocationOutcome.Ok($"No active fraud flags for '{entityId}'.")
                : McpToolInvocationOutcome.Ok(JsonSerializer.Serialize(profile.FraudFlags, jsonOptions));
            return Task.FromResult(outcome);
        })
    .AddTool(
        name: "list_high_risk_entities",
        description: "List all payees/entities currently classified as High risk. " +
                      "Useful for 'which payees are high risk right now' style questions.",
        inputSchema: Schema.Empty,
        handler: (_, _) =>
        {
            var highRisk = api.ListHighRisk();
            return Task.FromResult(McpToolInvocationOutcome.Ok(JsonSerializer.Serialize(highRisk, jsonOptions)));
        });

await host.RunAsync();

using System.Text.Json;
using AiOrchestrator.McpServers.Common;
using AiOrchestrator.McpServers.Payee;

const string ServerName = "payee-mcp-server";
var api = new MockPayeeApiClient();
var jsonOptions = new JsonSerializerOptions { WriteIndented = false };

var host = new McpServerHost(ServerName)
    .AddTool(
        name: "get_payee_by_id",
        description: "Look up a single payee by its unique payee ID and return its master-data record " +
                     "(name, masked account number, bank, country, status, last payment date).",
        inputSchema: Schema.Parse("""
            {
              "type": "object",
              "properties": {
                "payeeId": { "type": "string", "description": "Unique payee identifier, e.g. PAY-1001" }
              },
              "required": ["payeeId"]
            }
            """),
        handler: (args, _) =>
        {
            var payeeId = args.TryGetProperty("payeeId", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(payeeId))
            {
                return Task.FromResult(McpToolInvocationOutcome.Failure("payeeId is required."));
            }

            var payee = api.GetById(payeeId);
            var outcome = payee is null
                ? McpToolInvocationOutcome.Failure($"No payee found with id '{payeeId}'.")
                : McpToolInvocationOutcome.Ok(JsonSerializer.Serialize(payee, jsonOptions));
            return Task.FromResult(outcome);
        })
    .AddTool(
        name: "search_payees_by_name",
        description: "Search payee master data by a (partial, case-insensitive) name match. " +
                      "Useful when the user gives a company name instead of a payee ID.",
        inputSchema: Schema.Parse("""
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "Full or partial payee/company name to search for" }
              },
              "required": ["name"]
            }
            """),
        handler: (args, _) =>
        {
            var name = args.TryGetProperty("name", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return Task.FromResult(McpToolInvocationOutcome.Failure("name is required."));
            }

            var matches = api.SearchByName(name);
            var outcome = matches.Count == 0
                ? McpToolInvocationOutcome.Ok($"No payees found matching '{name}'.")
                : McpToolInvocationOutcome.Ok(JsonSerializer.Serialize(matches, jsonOptions));
            return Task.FromResult(outcome);
        })
    .AddTool(
        name: "list_recent_payees",
        description: "List the most recently paid payees, most recent first. " +
                      "Useful for 'what payees did we pay recently' style questions.",
        inputSchema: Schema.Parse("""
            {
              "type": "object",
              "properties": {
                "limit": { "type": "number", "description": "Maximum number of payees to return (default 5)" }
              }
            }
            """),
        handler: (args, _) =>
        {
            var limit = args.TryGetProperty("limit", out var v) && v.TryGetInt32(out var n) ? n : 5;
            var recent = api.ListRecent(limit);
            return Task.FromResult(McpToolInvocationOutcome.Ok(JsonSerializer.Serialize(recent, jsonOptions)));
        });

await host.RunAsync();

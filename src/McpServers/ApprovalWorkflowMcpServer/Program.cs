using System.Text.Json;
using AiOrchestrator.McpServers.ApprovalWorkflow;
using AiOrchestrator.McpServers.Common;

const string ServerName = "approval-workflow-mcp-server";
var api = new MockApprovalWorkflowApiClient();
var jsonOptions = new JsonSerializerOptions { WriteIndented = false };

var host = new McpServerHost(ServerName)
    .AddTool(
        name: "get_approval_status",
        description: "Get the full status of a single approval request by its request ID: type, amount, " +
                      "status, current approver, and remaining SLA hours.",
        inputSchema: Schema.Parse("""
            {
              "type": "object",
              "properties": {
                "requestId": { "type": "string", "description": "Approval request ID, e.g. APR-5001" }
              },
              "required": ["requestId"]
            }
            """),
        handler: (args, _) =>
        {
            var requestId = args.TryGetProperty("requestId", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return Task.FromResult(McpToolInvocationOutcome.Failure("requestId is required."));
            }

            var request = api.GetById(requestId);
            var outcome = request is null
                ? McpToolInvocationOutcome.Failure($"No approval request found with id '{requestId}'.")
                : McpToolInvocationOutcome.Ok(JsonSerializer.Serialize(request, jsonOptions));
            return Task.FromResult(outcome);
        })
    .AddTool(
        name: "list_pending_approvals",
        description: "List all pending approval requests, optionally filtered to a specific approver/queue.",
        inputSchema: Schema.Parse("""
            {
              "type": "object",
              "properties": {
                "approver": { "type": "string", "description": "Optional approver/queue name to filter by, e.g. finance-director" }
              }
            }
            """),
        handler: (args, _) =>
        {
            var approver = args.TryGetProperty("approver", out var v) ? v.GetString() : null;
            var pending = api.ListPending(approver);
            var outcome = pending.Count == 0
                ? McpToolInvocationOutcome.Ok("No pending approvals found for the given filter.")
                : McpToolInvocationOutcome.Ok(JsonSerializer.Serialize(pending, jsonOptions));
            return Task.FromResult(outcome);
        })
    .AddTool(
        name: "get_approval_history_for_entity",
        description: "Get all approval requests (any status) associated with a given payee/business entity ID. " +
                      "Useful for 'has this payee's payment/onboarding ever been approved or rejected' questions.",
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

            var history = api.HistoryForEntity(entityId);
            var outcome = history.Count == 0
                ? McpToolInvocationOutcome.Ok($"No approval history found for entity '{entityId}'.")
                : McpToolInvocationOutcome.Ok(JsonSerializer.Serialize(history, jsonOptions));
            return Task.FromResult(outcome);
        });

await host.RunAsync();

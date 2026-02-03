using FabricDataAgentRouter.Models;
using FabricDataAgentRouter.Services;

namespace FabricDataAgentRouter.Tools;

/// <summary>
/// Static tool definitions for Fabric Data Agent interactions
/// These are used to generate function definitions for the Foundry Agent
/// </summary>
public static class FabricAgentTools
{
    /// <summary>
    /// Query the Services Data Agent for Services-related information
    /// </summary>
    [Description("Query the Services Data Agent for information about Services, workorders, customers, and Services performance metrics.")]
    public static async Task<string> QueryServicesAgentAsync(
        [Description("The natural language query about Services data")] string query,
        FabricAgentConfig agent,
        FabricMcpClient mcpClient,
        CancellationToken cancellationToken = default)
    {
        var response = await mcpClient.QueryAgentAsync(agent, query, cancellationToken);
        return response.IsSuccess ? response.Content : $"Error: {response.ErrorMessage}";
    }

    /// <summary>
    /// Query the Employee Data Agent for human resources information
    /// </summary>
    [Description("Query the Employee Data Agent for information about employees, headcount, turnover, departments, and workforce analytics.")]
    public static async Task<string> QueryEmployeeAgentAsync(
        [Description("The natural language query about Employee data")] string query,
        FabricAgentConfig agent,
        FabricMcpClient mcpClient,
        CancellationToken cancellationToken = default)
    {
        var response = await mcpClient.QueryAgentAsync(agent, query, cancellationToken);
        return response.IsSuccess ? response.Content : $"Error: {response.ErrorMessage}";
    }

    /// <summary>
    /// Query the Product Data Agent for financial information
    /// </summary>
    [Description("Query the Product Data Agent for information about Product, inventory, supplier performance, order fulfillment, budget tracking, and warehouse management.")]
    public static async Task<string> QueryProductAgentAsync(
        [Description("The natural language query about Product data")] string query,
        FabricAgentConfig agent,
        FabricMcpClient mcpClient,
        CancellationToken cancellationToken = default)
    {
        var response = await mcpClient.QueryAgentAsync(agent, query, cancellationToken);
        return response.IsSuccess ? response.Content : $"Error: {response.ErrorMessage}";
    }

    /// <summary>
    /// List all available data agents
    /// </summary>
    [Description("List all available Fabric Data Agents and their capabilities")]
    public static string ListAvailableAgents(FabricAgentsConfig config)
    {
        var agents = config.Agents.Where(a => a.Enabled).ToList();

        if (!agents.Any())
        {
            return "No data agents are currently available.";
        }

        var lines = new List<string> { "Available Data Agents:\n" };

        foreach (var agent in agents)
        {
            lines.Add($"• {agent.Name}");
            lines.Add($"  Description: {agent.Description}");
            lines.Add($"  Domains: {string.Join(", ", agent.Domains)}");
            lines.Add($"  Data Sources: {string.Join(", ", agent.DataSources.Select(ds => ds.Name))}");
            lines.Add("");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Get details about a specific agent
    /// </summary>
    [Description("Get detailed information about a specific Fabric Data Agent")]
    public static string GetAgentDetails(
        [Description("The ID of the agent to get details for")] string agentId,
        FabricAgentsConfig config)
    {
        var agent = config.Agents.FirstOrDefault(a => a.Id == agentId);

        if (agent == null)
        {
            return $"Agent '{agentId}' not found.";
        }

        return $@"Agent: {agent.Name}
ID: {agent.Id}
Description: {agent.Description}
Status: {(agent.Enabled ? "Enabled" : "Disabled")}
Domains: {string.Join(", ", agent.Domains)}
Data Sources: {string.Join(", ", agent.DataSources.Select(ds => ds.Name))}
Example Queries:
{string.Join("\n", agent.ExampleQueries.Select(q => $"  • {q}"))}";
    }

    /// <summary>
    /// Generate function definitions for OpenAI-compatible function calling
    /// </summary>
    public static List<object> GenerateFunctionDefinitions(FabricAgentsConfig config)
    {
        var functions = new List<object>();

        foreach (var agent in config.Agents.Where(a => a.Enabled))
        {
            var functionName = $"query_{agent.Id.Replace("-", "_")}";

            functions.Add(new
            {
                name = functionName,
                description = $"Query the {agent.Name} for {agent.Description}. " +
                            $"Use this for questions about: {string.Join(", ", agent.Domains)}.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new
                        {
                            type = "string",
                            description = "The natural language query to send to the agent"
                        }
                    },
                    required = new[] { "query" }
                }
            });
        }

        // Add utility functions
        functions.Add(new
        {
            name = "list_available_agents",
            description = "List all available Fabric Data Agents and their capabilities",
            parameters = new
            {
                type = "object",
                properties = new { }
            }
        });

        functions.Add(new
        {
            name = "get_agent_details",
            description = "Get detailed information about a specific Fabric Data Agent",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    agent_id = new
                    {
                        type = "string",
                        description = "The ID of the agent to get details for"
                    }
                },
                required = new[] { "agent_id" }
            }
        });

        return functions;
    }
}
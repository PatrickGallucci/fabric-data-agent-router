namespace FabricDataAgentRouter.Models;

/// <summary>
/// Result of the routing decision
/// </summary>
public class RoutingResult
{
    /// <summary>
    /// The selected Fabric Data Agent configuration
    /// </summary>
    public FabricAgentConfig? SelectedAgent { get; set; }

    /// <summary>
    /// Confidence score for the routing decision (0.0 to 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Reasoning for the routing decision
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>
    /// Alternative agents considered with their scores
    /// </summary>
    public List<AgentScore> Alternatives { get; set; } = new();

    /// <summary>
    /// Whether the routing was successful
    /// </summary>
    public bool IsSuccessful => SelectedAgent != null && Confidence > 0;
}

/// <summary>
/// Score for an agent during routing evaluation
/// </summary>
public class AgentScore
{
    public string AgentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Response from a Fabric Data Agent
/// </summary>
public class FabricAgentResponse
{
    /// <summary>
    /// The agent that handled the request
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// The response content from the agent
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Whether the request was successful
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if the request failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Execution time in milliseconds
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Raw response data from MCP
    /// </summary>
    public object? RawResponse { get; set; }
}

/// <summary>
/// Complete response from the router including routing and agent response
/// </summary>
public class RouterResponse
{
    /// <summary>
    /// The original user query
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Routing decision details
    /// </summary>
    public RoutingResult Routing { get; set; } = new();

    /// <summary>
    /// Response from the Fabric Data Agent
    /// </summary>
    public FabricAgentResponse AgentResponse { get; set; } = new();

    /// <summary>
    /// Total execution time in milliseconds
    /// </summary>
    public long TotalExecutionTimeMs { get; set; }

    /// <summary>
    /// Conversation thread ID for context continuity
    /// </summary>
    public string? ThreadId { get; set; }
}
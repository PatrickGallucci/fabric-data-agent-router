using System.Text.Json.Serialization;

namespace FabricDataAgentRouter.Models;

/// <summary>
/// Root configuration containing all Fabric Data Agents
/// </summary>
public class FabricAgentsConfig
{
    [JsonPropertyName("agents")]
    public List<FabricAgentConfig> Agents { get; set; } = new();

    [JsonPropertyName("routing")]
    public RoutingConfig Routing { get; set; } = new();

    [JsonPropertyName("authentication")]
    public AuthConfig Authentication { get; set; } = new();
}

/// <summary>
/// Configuration for a single Fabric Data Agent
/// </summary>
public class FabricAgentConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("mcpServerUrl")]
    public string McpServerUrl { get; set; } = string.Empty;

    [JsonPropertyName("workspaceId")]
    public string WorkspaceId { get; set; } = string.Empty;

    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    [JsonPropertyName("domains")]
    public List<string> Domains { get; set; } = new();

    [JsonPropertyName("exampleQueries")]
    public List<string> ExampleQueries { get; set; } = new();

    [JsonPropertyName("dataSources")]
    public List<DataSourceConfig> DataSources { get; set; } = new();

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets the resolved MCP URL with actual workspace and agent IDs
    /// </summary>
    public string GetResolvedMcpUrl()
    {
        return McpServerUrl
            .Replace("{workspaceId}", WorkspaceId)
            .Replace("{agentId}", AgentId);
    }
}

/// <summary>
/// Configuration for a data source connected to an agent
/// </summary>
public class DataSourceConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("connectionString")]
    public string ConnectionString { get; set; } = string.Empty;

    [JsonPropertyName("schema")]
    public string Schema { get; set; } = string.Empty;

    [JsonPropertyName("tables")]
    public List<string> Tables { get; set; } = new();

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Routing configuration settings
/// </summary>
public class RoutingConfig
{
    [JsonPropertyName("confidenceThreshold")]
    public double ConfidenceThreshold { get; set; } = 0.7;

    [JsonPropertyName("defaultAgentId")]
    public string DefaultAgentId { get; set; } = string.Empty;

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
/// Authentication configuration
/// </summary>
public class AuthConfig
{
    [JsonPropertyName("useManagedIdentity")]
    public bool UseManagedIdentity { get; set; } = true;

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;
}
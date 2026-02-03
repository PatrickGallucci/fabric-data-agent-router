using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Azure.Core;
using Azure.Identity;
using FabricDataAgentRouter.Models;

namespace FabricDataAgentRouter.Services;

/// <summary>
/// Client for communicating with Fabric Data Agents via MCP protocol
/// </summary>
public class FabricMcpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FabricMcpClient> _logger;
    private readonly DefaultAzureCredential _credential;
    private AccessToken? _cachedToken;
    private static readonly string[] FabricScopes = { "https://analysis.windows.net/powerbi/api/.default" };

    public FabricMcpClient(ILogger<FabricMcpClient> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _credential = new DefaultAzureCredential();
    }

    /// <summary>
    /// Query a Fabric Data Agent using MCP protocol
    /// </summary>
    public async Task<FabricAgentResponse> QueryAgentAsync(
        FabricAgentConfig agent,
        string query,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = new FabricAgentResponse { AgentId = agent.Id };

        try
        {
            var mcpUrl = agent.GetResolvedMcpUrl();
            _logger.LogInformation("Querying agent {AgentName} at {McpUrl}", agent.Name, mcpUrl);

            // Get access token for Fabric API
            var token = await GetAccessTokenAsync(cancellationToken);

            // Build MCP request
            var mcpRequest = new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString(),
                method = "tools/call",
                @params = new
                {
                    name = "query",
                    arguments = new
                    {
                        query = query
                    }
                }
            };

            var requestJson = JsonSerializer.Serialize(mcpRequest);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, mcpUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = content;

            var httpResponse = await _httpClient.SendAsync(request, cancellationToken);

            if (httpResponse.IsSuccessStatusCode)
            {
                var responseContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                var mcpResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                // Extract result from MCP response
                if (mcpResponse.TryGetProperty("result", out var result))
                {
                    if (result.TryGetProperty("content", out var resultContent) &&
                        resultContent.ValueKind == JsonValueKind.Array &&
                        resultContent.GetArrayLength() > 0)
                    {
                        var firstContent = resultContent[0];
                        if (firstContent.TryGetProperty("text", out var textElement))
                        {
                            response.Content = textElement.GetString() ?? string.Empty;
                        }
                    }
                    response.IsSuccess = true;
                    response.RawResponse = mcpResponse;
                }
                else if (mcpResponse.TryGetProperty("error", out var error))
                {
                    response.IsSuccess = false;
                    response.ErrorMessage = error.TryGetProperty("message", out var msg)
                        ? msg.GetString()
                        : "Unknown MCP error";
                }
            }
            else
            {
                response.IsSuccess = false;
                response.ErrorMessage = $"HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}";
                _logger.LogError("Agent query failed: {Error}", response.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error querying agent {AgentId}", agent.Id);
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
        }

        return response;
    }

    /// <summary>
    /// Test connectivity to a Fabric Data Agent
    /// </summary>
    public async Task<bool> TestConnectivityAsync(
        FabricAgentConfig agent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var mcpUrl = agent.GetResolvedMcpUrl();
            var token = await GetAccessTokenAsync(cancellationToken);

            // Send MCP initialize request
            var mcpRequest = new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString(),
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "FabricDataAgentRouter",
                        version = "1.0.0"
                    }
                }
            };

            var requestJson = JsonSerializer.Serialize(mcpRequest);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, mcpUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = content;

            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connectivity test failed for agent {AgentId}", agent.Id);
            return false;
        }
    }

    /// <summary>
    /// List available tools from a Fabric Data Agent
    /// </summary>
    public async Task<List<string>> ListToolsAsync(
        FabricAgentConfig agent,
        CancellationToken cancellationToken = default)
    {
        var tools = new List<string>();

        try
        {
            var mcpUrl = agent.GetResolvedMcpUrl();
            var token = await GetAccessTokenAsync(cancellationToken);

            var mcpRequest = new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString(),
                method = "tools/list"
            };

            var requestJson = JsonSerializer.Serialize(mcpRequest);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, mcpUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = content;

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var mcpResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                if (mcpResponse.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("tools", out var toolsArray) &&
                    toolsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tool in toolsArray.EnumerateArray())
                    {
                        if (tool.TryGetProperty("name", out var name))
                        {
                            tools.Add(name.GetString() ?? string.Empty);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list tools for agent {AgentId}", agent.Id);
        }

        return tools;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        // Check if cached token is still valid (with 5-minute buffer)
        if (_cachedToken.HasValue &&
            _cachedToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _cachedToken.Value.Token;
        }

        // Get new token
        _cachedToken = await _credential.GetTokenAsync(
            new TokenRequestContext(FabricScopes),
            cancellationToken);

        _logger.LogDebug("Acquired new Fabric API access token");
        return _cachedToken.Value.Token;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
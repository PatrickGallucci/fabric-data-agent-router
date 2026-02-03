using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using FabricDataAgentRouter.Models;

namespace FabricDataAgentRouter.Services;

/// <summary>
/// Main orchestrating service that routes queries to the appropriate Fabric Data Agent
/// using Azure AI Foundry Agent Service with function calling.
/// Uses Azure.AI.Agents.Persistent SDK (GA 1.1.0)
/// </summary>
public class RouterAgentService : IDisposable
{
    private readonly ILogger<RouterAgentService> _logger;
    private readonly FabricAgentsConfig _config;
    private readonly FabricMcpClient _mcpClient;
    private readonly IntentClassifier _intentClassifier;
    private readonly string _projectEndpoint;
    private readonly string _modelDeployment;

    private PersistentAgentsClient? _agentsClient;
    private PersistentAgent? _routerAgent;
    private string? _currentThreadId;

    public RouterAgentService(
        ILogger<RouterAgentService> logger,
        FabricAgentsConfig config,
        FabricMcpClient mcpClient,
        IntentClassifier intentClassifier,
        string projectEndpoint,
        string modelDeployment)
    {
        _logger = logger;
        _config = config;
        _mcpClient = mcpClient;
        _intentClassifier = intentClassifier;
        _projectEndpoint = projectEndpoint;
        _modelDeployment = modelDeployment;
    }

    /// <summary>
    /// Initialize the Router Agent with function tools for each Fabric Data Agent
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var credential = new InteractiveBrowserCredential();
        _logger.LogInformation("Initializing Router Agent Service...");

        // Create Persistent Agents Client
        _agentsClient = new PersistentAgentsClient(_projectEndpoint, credential);

        // Build function tools for each enabled agent
        var tools = BuildFunctionTools().ToList();

        // Create the router agent - async returns Response<PersistentAgent>, use .Value
        Response<PersistentAgent> agentResponse = await _agentsClient.Administration.CreateAgentAsync(
            model: _modelDeployment,
            name: "FabricDataAgentRouter",
            instructions: GetRouterInstructions(),
            tools: tools);

        _routerAgent = agentResponse.Value;

        _logger.LogInformation("Router Agent created: {AgentId}", _routerAgent.Id);
    }

    /// <summary>
    /// Process a user query and route to appropriate Fabric Data Agent
    /// </summary>
    public async Task<RouterResponse> ProcessQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var response = new RouterResponse { Query = query };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (_agentsClient == null || _routerAgent == null)
            {
                throw new InvalidOperationException("Router Agent not initialized. Call InitializeAsync first.");
            }

            // Create or reuse thread
            if (string.IsNullOrEmpty(_currentThreadId))
            {
                Response<PersistentAgentThread> threadResponse = await _agentsClient.Threads.CreateThreadAsync();
                _currentThreadId = threadResponse.Value.Id;
                _logger.LogInformation("Created new thread: {ThreadId}", _currentThreadId);
            }
            response.ThreadId = _currentThreadId;

            // Add user message - returns Response<PersistentThreadMessage>
            await _agentsClient.Messages.CreateMessageAsync(
                _currentThreadId,
                MessageRole.User,
                query);

            // Create run - returns Response<ThreadRun>
            Response<ThreadRun> runResponse = await _agentsClient.Runs.CreateRunAsync(
                _currentThreadId,
                _routerAgent.Id);

            ThreadRun run = runResponse.Value;

            // Handle the run with potential function calls
            var agentResponse = await HandleRunWithFunctionCallsAsync(run, cancellationToken);
            response.AgentResponse = agentResponse;

            // Set routing info
            response.Routing = new RoutingResult
            {
                SelectedAgent = _config.Agents.FirstOrDefault(a => a.Id == agentResponse.AgentId),
                Confidence = 1.0, // Function calling implies confident selection
                Reasoning = "Selected via Foundry Agent function calling"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing query");
            response.AgentResponse = new FabricAgentResponse
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            stopwatch.Stop();
            response.TotalExecutionTimeMs = stopwatch.ElapsedMilliseconds;
        }

        return response;
    }

    /// <summary>
    /// Handle run with function calling loop
    /// </summary>
    private async Task<FabricAgentResponse> HandleRunWithFunctionCallsAsync(
        ThreadRun run,
        CancellationToken cancellationToken)
    {
        var agentResponse = new FabricAgentResponse();
        var functionStopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress || run.Status == RunStatus.RequiresAction)
        {
            // Check for cancellation
            cancellationToken.ThrowIfCancellationRequested();

            if (run.Status == RunStatus.RequiresAction &&
                run.RequiredAction is SubmitToolOutputsAction toolAction)
            {
                // Process function calls
                var toolOutputs = new List<ToolOutput>();

                foreach (var toolCall in toolAction.ToolCalls)
                {
                    if (toolCall is RequiredFunctionToolCall functionCall)
                    {
                        _logger.LogInformation(
                            "Executing function: {FunctionName} with args: {Args}",
                            functionCall.Name, functionCall.Arguments);

                        var result = await ExecuteFunctionCallAsync(
                            functionCall.Name,
                            functionCall.Arguments,
                            cancellationToken);

                        toolOutputs.Add(new ToolOutput(functionCall.Id, result));

                        // Track which agent was called
                        var agentId = ExtractAgentIdFromFunctionName(functionCall.Name);
                        if (!string.IsNullOrEmpty(agentId))
                        {
                            agentResponse.AgentId = agentId;
                        }
                    }
                }

                // Submit tool outputs - pass the ThreadRun object directly
                run = await _agentsClient!.Runs.SubmitToolOutputsToRunAsync(run, toolOutputs);
            }
            else
            {
                // Wait and poll for status
                await Task.Delay(500, cancellationToken);
                run = await _agentsClient!.Runs.GetRunAsync(_currentThreadId!, run.Id);
            }
        }

        functionStopwatch.Stop();
        agentResponse.ExecutionTimeMs = functionStopwatch.ElapsedMilliseconds;

        if (run.Status == RunStatus.Completed)
        {
            // Get the assistant's response using synchronous pagination
            Pageable<PersistentThreadMessage> messages = _agentsClient!.Messages.GetMessages(
                threadId: _currentThreadId!,
                order: ListSortOrder.Descending);

            PersistentThreadMessage? lastAssistantMessage = null;
            foreach (var message in messages)
            {
                if (message.Role == MessageRole.Agent)
                {
                    lastAssistantMessage = message;
                    break; // First one is most recent due to descending order
                }
            }

            if (lastAssistantMessage != null)
            {
                foreach (var content in lastAssistantMessage.ContentItems)
                {
                    if (content is MessageTextContent textContent)
                    {
                        agentResponse.Content = textContent.Text;
                        agentResponse.IsSuccess = true;
                        break;
                    }
                }
            }
        }
        else
        {
            agentResponse.IsSuccess = false;
            agentResponse.ErrorMessage = $"Run ended with status: {run.Status}";
            if (run.LastError != null)
            {
                agentResponse.ErrorMessage += $" - {run.LastError.Message}";
            }
        }

        return agentResponse;
    }

    /// <summary>
    /// Execute a function call by routing to the appropriate Fabric Data Agent
    /// </summary>
    private async Task<string> ExecuteFunctionCallAsync(
        string functionName,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var arguments = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            var query = arguments.TryGetProperty("query", out var q) ? q.GetString() : null;

            // Handle list_available_agents function
            if (functionName == "list_available_agents")
            {
                var agentList = _config.Agents
                    .Where(a => a.Enabled)
                    .Select(a => new { name = a.Name, description = a.Description, domains = a.Domains })
                    .ToList();
                return JsonSerializer.Serialize(new { agents = agentList });
            }

            if (string.IsNullOrEmpty(query))
            {
                return JsonSerializer.Serialize(new { error = "Missing query parameter" });
            }

            // Find the agent by function name
            var agentId = ExtractAgentIdFromFunctionName(functionName);
            var agent = _config.Agents.FirstOrDefault(a => a.Id == agentId);

            if (agent == null)
            {
                return JsonSerializer.Serialize(new { error = $"Agent not found: {agentId}" });
            }

            // Call the Fabric Data Agent via MCP
            var fabricResponse = await _mcpClient.QueryAgentAsync(agent, query, cancellationToken);

            if (fabricResponse.IsSuccess)
            {
                return fabricResponse.Content;
            }
            else
            {
                return JsonSerializer.Serialize(new { error = fabricResponse.ErrorMessage });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing function {FunctionName}", functionName);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Extract agent ID from function name (e.g., "query_Services_agent" -> "Services-agent")
    /// </summary>
    private string ExtractAgentIdFromFunctionName(string functionName)
    {
        // Function names are like "query_Services_agent" -> agent ID is "Services-agent"
        if (functionName.StartsWith("query_") && functionName.EndsWith("_agent"))
        {
            var middle = functionName.Substring(6, functionName.Length - 12); // Remove "query_" and "_agent"
            return $"{middle}-agent";
        }
        return functionName;
    }

    /// <summary>
    /// Build function tool definitions for each enabled Fabric Data Agent
    /// </summary>
    private IEnumerable<ToolDefinition> BuildFunctionTools()
    {
        var tools = new List<ToolDefinition>();

        foreach (var agent in _config.Agents.Where(a => a.Enabled))
        {
            var functionName = $"query_{agent.Id.Replace("-", "_")}";

            var functionDef = new FunctionToolDefinition(
                name: functionName,
                description: $"Query the {agent.Name} for {agent.Description}. " +
                            $"Use this for questions about: {string.Join(", ", agent.Domains)}.",
                parameters: BinaryData.FromObjectAsJson(new
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
                }));

            tools.Add(functionDef);
            _logger.LogDebug("Added function tool: {FunctionName}", functionName);
        }

        // Add utility function to list available agents
        tools.Add(new FunctionToolDefinition(
            name: "list_available_agents",
            description: "List all available data agents and their capabilities"));

        return tools;
    }

    /// <summary>
    /// Get the system instructions for the router agent
    /// </summary>
    private string GetRouterInstructions()
    {
        var agentDescriptions = string.Join("\n", _config.Agents
            .Where(a => a.Enabled)
            .Select(a => $"- {a.Name}: {a.Description} (domains: {string.Join(", ", a.Domains)})"));

        return $@"You are an intelligent query router that directs user questions to the most appropriate Fabric Data Agent.

Available Data Agents:
{agentDescriptions}

Your responsibilities:
1. Analyze each user query to understand the intent and domain
2. Select the most appropriate data agent based on the query content
3. Call the corresponding function to get the answer
4. Present the response clearly to the user

Routing Guidelines:
- Match queries to agents based on domain keywords and context
- If a query spans multiple domains, choose the primary domain or ask for clarification
- If no agent is clearly appropriate, use the default agent or ask the user to clarify
- Always explain which agent you're using and why

Response Format:
- Provide clear, concise answers based on the agent's response
- If the agent returns an error, explain it to the user and suggest alternatives
- Maintain conversation context for follow-up questions";
    }

    /// <summary>
    /// Start a new conversation thread
    /// </summary>
    public void ClearThread()
    {
        _currentThreadId = null;
        _logger.LogInformation("Conversation thread cleared");
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}
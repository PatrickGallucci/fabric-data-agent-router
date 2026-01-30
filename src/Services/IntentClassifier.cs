using FabricDataAgentRouter.Models;
using Microsoft.Extensions.Logging;

namespace FabricDataAgentRouter.Services;

/// <summary>
/// Classifies user intent and selects the appropriate Fabric Data Agent
/// </summary>
public class IntentClassifier
{
    private readonly ILogger<IntentClassifier> _logger;
    private readonly FabricMcpClient _mcpClient;

    public IntentClassifier(ILogger<IntentClassifier> logger, FabricMcpClient mcpClient)
    {
        _logger = logger;
        _mcpClient = mcpClient;
    }

    /// <summary>
    /// Classify the user query and return routing result
    /// </summary>
    public async Task<RoutingResult> ClassifyIntent(
        string query,
        FabricAgentsConfig config,
        CancellationToken cancellationToken = default)
    {
        var result = new RoutingResult();
        var enabledAgents = config.Agents.Where(a => a.Enabled).ToList();

        if (!enabledAgents.Any())
        {
            _logger.LogWarning("No enabled agents found");
            result.Reasoning = "No enabled agents available";
            return result;
        }

        // Score each agent
        var scores = new List<AgentScore>();
        foreach (var agent in enabledAgents)
        {
            var score = CalculateRelevanceScore(query, agent);
            scores.Add(new AgentScore
            {
                AgentId = agent.Id,
                AgentName = agent.Name,
                Score = score.score,
                Reason = score.reason
            });
        }

        // Sort by score descending
        scores = scores.OrderByDescending(s => s.Score).ToList();
        result.Alternatives = scores;

        var topScore = scores.First();

        // Check confidence threshold
        if (topScore.Score >= config.Routing.ConfidenceThreshold)
        {
            result.SelectedAgent = enabledAgents.First(a => a.Id == topScore.AgentId);
            result.Confidence = topScore.Score;
            result.Reasoning = topScore.Reason;
            _logger.LogInformation(
                "Selected agent {AgentName} with confidence {Confidence:P0}",
                result.SelectedAgent.Name, result.Confidence);
        }
        else if (!string.IsNullOrEmpty(config.Routing.DefaultAgentId))
        {
            // Fall back to default agent
            result.SelectedAgent = enabledAgents.FirstOrDefault(a => a.Id == config.Routing.DefaultAgentId);
            result.Confidence = topScore.Score;
            result.Reasoning = $"Below confidence threshold ({config.Routing.ConfidenceThreshold:P0}), using default agent";
            _logger.LogInformation(
                "Using default agent {AgentId} (confidence {Confidence:P0} below threshold)",
                config.Routing.DefaultAgentId, result.Confidence);
        }
        else
        {
            // Use top scoring agent even below threshold
            result.SelectedAgent = enabledAgents.First(a => a.Id == topScore.AgentId);
            result.Confidence = topScore.Score;
            result.Reasoning = $"Best match (below threshold): {topScore.Reason}";
            _logger.LogInformation(
                "Selected best match {AgentName} with low confidence {Confidence:P0}",
                result.SelectedAgent.Name, result.Confidence);
        }

        return result;
    }

    /// <summary>
    /// Calculate relevance score for an agent based on the query
    /// </summary>
    private (double score, string reason) CalculateRelevanceScore(string query, FabricAgentConfig agent)
    {
        var queryLower = query.ToLowerInvariant();
        var reasons = new List<string>();
        double totalScore = 0;

        // Domain keyword matching (weight: 0.4)
        var domainMatches = agent.Domains.Count(d =>
            queryLower.Contains(d.ToLowerInvariant()));
        if (domainMatches > 0)
        {
            var domainScore = Math.Min(domainMatches * 0.2, 0.4);
            totalScore += domainScore;
            reasons.Add($"Domain match: {domainMatches} keywords");
        }

        // Example query similarity (weight: 0.3)
        var exampleScore = CalculateExampleSimilarity(queryLower, agent.ExampleQueries);
        if (exampleScore > 0)
        {
            totalScore += exampleScore * 0.3;
            reasons.Add($"Similar to example queries ({exampleScore:P0})");
        }

        // Description relevance (weight: 0.2)
        var descriptionScore = CalculateDescriptionRelevance(queryLower, agent.Description);
        if (descriptionScore > 0)
        {
            totalScore += descriptionScore * 0.2;
            reasons.Add($"Description relevance ({descriptionScore:P0})");
        }

        // Data source mention (weight: 0.1)
        var dataSourceMatches = agent.DataSources.Count(ds =>
            queryLower.Contains(ds.Name.ToLowerInvariant()));
        if (dataSourceMatches > 0)
        {
            totalScore += 0.1;
            reasons.Add($"Data source match");
        }

        var reason = reasons.Any() ? string.Join("; ", reasons) : "No strong matches";
        return (Math.Min(totalScore, 1.0), reason);
    }

    /// <summary>
    /// Calculate similarity between query and example queries
    /// </summary>
    private double CalculateExampleSimilarity(string query, List<string> examples)
    {
        if (!examples.Any()) return 0;

        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();

        var maxSimilarity = 0.0;

        foreach (var example in examples)
        {
            var exampleWords = example.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet();

            var intersection = queryWords.Intersect(exampleWords).Count();
            var union = queryWords.Union(exampleWords).Count();

            if (union > 0)
            {
                var similarity = (double)intersection / union; // Jaccard similarity
                maxSimilarity = Math.Max(maxSimilarity, similarity);
            }
        }

        return maxSimilarity;
    }

    /// <summary>
    /// Calculate relevance between query and agent description
    /// </summary>
    private double CalculateDescriptionRelevance(string query, string description)
    {
        if (string.IsNullOrEmpty(description)) return 0;

        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length > 2) // Skip short words
            .ToHashSet();

        var descLower = description.ToLowerInvariant();
        var matchCount = queryWords.Count(w => descLower.Contains(w));

        return queryWords.Count > 0 ? (double)matchCount / queryWords.Count : 0;
    }

    /// <summary>
    /// Validate connectivity to all enabled agents
    /// </summary>
    public async Task<Dictionary<string, bool>> ValidateAgentsAsync(
        FabricAgentsConfig config,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, bool>();

        foreach (var agent in config.Agents.Where(a => a.Enabled))
        {
            var isConnected = await _mcpClient.TestConnectivityAsync(agent, cancellationToken);
            results[agent.Id] = isConnected;
            _logger.LogInformation(
                "Agent {AgentName} connectivity: {Status}",
                agent.Name, isConnected ? "OK" : "FAILED");
        }

        return results;
    }
}
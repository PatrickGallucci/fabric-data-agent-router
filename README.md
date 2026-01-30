# Fabric Data Agent Router

Routes natural language queries to the appropriate Microsoft Fabric Data Agent using Azure AI Foundry Agent Service with function calling.

## Architecture

```
User Query → Foundry Agent → Function Call → Fabric Data Agent (MCP) → Response
```

The Foundry Agent analyzes the query and uses function calling to route to the appropriate Fabric Data Agent:
- **Services Agent** - Revenue, orders, customers, pipeline
- **Employee Agent** - Headcount, turnover, departments, workforce  
- **Product Agent** - Budget, P&L, expenses, accounting. inventory, warehouse

## Prerequisites

1. **.NET 8 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
2. **Azure CLI** - For authentication
3. **Azure AI Foundry** resource with deployed GPT-4o model
4. **Microsoft Fabric** workspace with published Data Agents

## Quick Start

```powershell
# 1. Clone/download the project

# 2. Navigate to project
cd src/fabric-data-agent-router

# 3. Update appsettings.json with your Foundry endpoint
#    "ProjectEndpoint": "https://YOUR-RESOURCE.cognitiveservices.azure.com"

# 4. Update config/fabric-agents.json with your Fabric workspace/agent IDs

# 5. Login to Azure
az login

# 6. Build and run
dotnet restore
dotnet build
dotnet run
```

## Configuration

### appsettings.json

```json
{
  "AzureAIFoundry": {
    "ProjectEndpoint": "https://YOUR-FOUNDRY-RESOURCE.cognitiveservices.azure.com",
    "ModelDeploymentName": "gpt-4o"
  }
}
```

### config/fabric-agents.json

Update each agent with your actual Fabric workspace and agent IDs:

```json
{
  "agents": [
    {
      "id": "Services-agent",
      "name": "ServicesDataAgent",
      "workspaceId": "YOUR-WORKSPACE-GUID",
      "agentId": "YOUR-AGENT-GUID",
      ...
    }
  ]
}
```

## Getting Fabric Agent IDs

1. Open Microsoft Fabric → Your Workspace
2. Open the Data Agent → Settings → **Model Context Protocol** tab
3. Copy the **MCP Server URL**, **Workspace ID**, and **Agent ID**

## Project Structure

```
foundry-fabric-router/
├── config/
│   └── fabric-agents.json      # Agent configuration
└── src/fabric-data-agent-router/
    ├── Models/
    │   ├── FabricAgentConfig.cs    # Configuration models
    │   └── RoutingResult.cs        # Response models
    ├── Services/
    │   ├── FabricMcpClient.cs      # MCP protocol client
    │   ├── IntentClassifier.cs     # Query classification
    │   └── RouterAgentService.cs   # Main orchestrator
    ├── Tools/
    │   └── FabricAgentTools.cs     # Function definitions
    ├── Program.cs                   # Entry point
    ├── appsettings.json            # App configuration
    └── fabric-data-agent-router.csproj
```

## Usage

```
╔═══════════════════════════════════════════════════════════════════════════════╗
║         FOUNDRY → FABRIC DATA AGENT ROUTER                                    ║
╚═══════════════════════════════════════════════════════════════════════════════╝

You: What were our Q4 Services numbers?

Routing query...
  ╰─ Routed to: ServicesDataAgent
  ╰─ Confidence: 100%
  ╰─ Time: 2341ms

Router: Based on the Q4 Services data...
```

### Commands

| Command | Description |
|---------|-------------|
| `help` | Show example queries |
| `clear` | Start new conversation |
| `quit` | Exit application |

## Troubleshooting

### "ProjectEndpoint not configured"
Update `appsettings.json` with your Foundry endpoint.

### "DefaultAzureCredential authentication failed"  
Run `az login` to authenticate.

### "Agent not found"
Verify `fabric-agents.json` has correct workspace/agent GUIDs.

### "401 Unauthorized" from Fabric
Ensure your Fabric Data Agent is **Published** (not draft).

## How It Works

1. **User submits query** → Foundry Agent receives it
2. **Agent analyzes intent** → Selects appropriate function tool
3. **Function call triggered** → `requires_action` status returned
4. **Router executes function** → Calls Fabric MCP endpoint
5. **Response submitted** → Agent formulates final answer
6. **User receives response** → With routing metadata

The function calling pattern allows the Foundry Agent to intelligently route without explicit classification rules.

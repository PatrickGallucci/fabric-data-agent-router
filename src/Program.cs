using FabricDataAgentRouter.Models;
using FabricDataAgentRouter.Services;


namespace FabricDataAgentRouter;

/// <summary>
/// Entry point for the Foundry Fabric Router application
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine(@"
______________________________________________________________________________
                                                                               
    FABRIC DATA AGENT ROUTER                                    
    Intelligent Query Routing for Enterprise Data                         
______________________________________________________________________________                                                                               

");

        var host = CreateHostBuilder(args).Build();

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Create DI container
        IServiceCollection appServices = new ServiceCollection();
        appServices.AddSingleton<IConfiguration>(configuration);

        // Add logging
        appServices.AddLogging(loggingBuilder =>
        {
            loggingBuilder.AddConsole();
            loggingBuilder.SetMinimumLevel(LogLevel.Information);
        });

        // Add Application Insights
        appServices.AddApplicationInsightsTelemetryWorkerService(options =>
        {
            options.ConnectionString = configuration["ApplicationInsights:ConnectionString"];
        });

        // Build service provider
        IServiceProvider serviceProvider = appServices.BuildServiceProvider();

        // Get services
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var telemetryClient = serviceProvider.GetRequiredService<TelemetryClient>();

        try
        {
            logger.LogInformation("Application starting...");

            // var logger = services.GetRequiredService<ILogger<Program>>();
            // var configuration = services.GetRequiredService<IConfiguration>();

            // Load Fabric agents configuration
            var configPath = configuration["FabricAgentsConfigPath"] ?? "../config/fabric-agents.json";
            var fabricConfig = await LoadFabricAgentsConfigAsync(configPath);

            logger.LogInformation("Loaded {Count} Fabric Data Agents", fabricConfig.Agents.Count);

            // Get Foundry settings
            var projectEndpoint = configuration["AzureAIFoundry:ProjectEndpoint"]
                ?? throw new InvalidOperationException("AzureAIFoundry:ProjectEndpoint not configured");
            var modelDeployment = configuration["AzureAIFoundry:ModelDeploymentName"] ?? "gpt-4o";

            // Create services
            var mcpClient = new FabricMcpClient(services.GetRequiredService<ILogger<FabricMcpClient>>());
            var intentClassifier = new IntentClassifier(
                services.GetRequiredService<ILogger<IntentClassifier>>(),
                mcpClient);

            var routerService = new RouterAgentService(
                services.GetRequiredService<ILogger<RouterAgentService>>(),
                fabricConfig,
                mcpClient,
                intentClassifier,
                projectEndpoint,
                modelDeployment);

            // Initialize the router agent
            logger.LogInformation("Initializing Router Agent...");
            await routerService.InitializeAsync();

            // Run interactive mode
            await RunInteractiveModeAsync(routerService, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Application error");
            telemetryClient.TrackException(ex);
            Console.WriteLine($"\nError: {ex.Message}");

            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner: {ex.InnerException.Message}");
            }

            Environment.Exit(1);
        }
        finally
        {
            // CRITICAL: Flush telemetry before exit
            telemetryClient.Flush();
            await Task.Delay(5000);
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
                    .AddEnvironmentVariables()
                    .AddCommandLine(args);
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(context.Configuration);
            });

    private static async Task<FabricAgentsConfig> LoadFabricAgentsConfigAsync(string configPath)
    {
        if (!File.Exists(configPath))
        {
            // Try relative to executable
            var exePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            configPath = Path.Combine(exePath ?? ".", "config", "fabric-agents.json");
        }

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Fabric agents configuration not found: {configPath}");
        }

        var json = await File.ReadAllTextAsync(configPath);
        var config = JsonSerializer.Deserialize<FabricAgentsConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return config ?? throw new InvalidOperationException("Failed to parse fabric-agents.json");
    }

    private static async Task RunInteractiveModeAsync(
        RouterAgentService routerService,
        ILogger logger)
    {
        Console.WriteLine(@"
______________________________________________________________________________

  Interactive Mode - Ask questions to route to Fabric Data Agents            
  Commands: 'quit' to exit, 'clear' for new conversation, 'help' for info    
______________________________________________________________________________
");

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("\nYou: ");
            Console.ResetColor();

            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
                continue;

            // Handle commands
            switch (input.ToLowerInvariant())
            {
                case "quit":
                case "exit":
                    Console.WriteLine("\nGoodbye!");
                    return;

                case "clear":
                    routerService.ClearThread();
                    Console.WriteLine("\n[Conversation cleared - starting fresh]");
                    continue;

                case "help":
                    PrintHelp();
                    continue;
            }

            // Process query
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\nRouting query...");
            Console.ResetColor();

            try
            {
                var response = await routerService.ProcessQueryAsync(input);

                // Show routing info
                if (response.Routing.SelectedAgent != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"  ╰─ Routed to: {response.Routing.SelectedAgent.Name}");
                    Console.WriteLine($"  ╰─ Confidence: {response.Routing.Confidence:P0}");
                    Console.WriteLine($"  ╰─ Time: {response.TotalExecutionTimeMs}ms");
                    Console.ResetColor();
                }

                // Show response
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("\nRouter: ");
                Console.ResetColor();

                if (response.AgentResponse.IsSuccess)
                {
                    Console.WriteLine(response.AgentResponse.Content);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: {response.AgentResponse.ErrorMessage}");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nError: {ex.Message}");
                Console.ResetColor();
                logger.LogError(ex, "Error processing query");
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"

______________________________________________________________________________

  HELP - Foundry Fabric Router                                                 
                                                                               
  Commands:                                                                    
    quit, exit  - Exit the application                                         
    clear       - Start a new conversation (clears context)                    
    help        - Show this help message                                       
                                                                               
  Example Queries:                                                             
    Services:   ""What were our Q4 Services numbers?""                              
                ""Show me top 10 customers by revenue""                          
    Employee: ""What's our current headcount?""                                
                ""Show turnover rates by department""                            
    Product:      ""What are current inventory levels?""                           
                ""Show production metrics for this week""                        
______________________________________________________________________________

");
    }

}
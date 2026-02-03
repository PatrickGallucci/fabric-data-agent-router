<#
.SYNOPSIS
    Sets environment variables for local development of the Fabric Data Agent Router.

.DESCRIPTION
    This script configures all required environment variables for running the
    Fabric Data Agent Router locally. It supports multiple configuration modes:
    - Interactive prompts for missing values
    - Loading from appsettings.json
    - Loading from .env file
    - Command-line parameters

.PARAMETER ProjectEndpoint
    Azure AI Foundry project endpoint URL.

.PARAMETER ModelDeploymentName
    Name of the deployed model in Azure OpenAI (e.g., gpt-4o-deployment).

.PARAMETER AppInsightsConnectionString
    Application Insights connection string for telemetry.

.PARAMETER OpenAIEndpoint
    Azure OpenAI service endpoint URL.

.PARAMETER ConfigFile
    Path to appsettings.json or .env file to load configuration from.

.PARAMETER Persist
    Save environment variables to user profile (persists across sessions).

.PARAMETER Show
    Display current environment variable values without setting them.

.PARAMETER Clear
    Clear all Fabric Router environment variables.

.EXAMPLE
    .\Set-DevEnvironment.ps1
    # Interactive mode - prompts for missing values

.EXAMPLE
    .\Set-DevEnvironment.ps1 -ConfigFile ".\appsettings.Development.json"
    # Load from configuration file

.EXAMPLE
    .\Set-DevEnvironment.ps1 -ProjectEndpoint "https://..." -ModelDeploymentName "gpt-4o"
    # Set specific values via parameters

.EXAMPLE
    .\Set-DevEnvironment.ps1 -Show
    # Display current configuration

.EXAMPLE
    .\Set-DevEnvironment.ps1 -Persist
    # Save to user environment (survives terminal close)

.NOTES
    Author: Fabric Router Team
    Version: 1.0.0
#>

[CmdletBinding(DefaultParameterSetName = 'Set')]
param(
    [Parameter(ParameterSetName = 'Set')]
    [string]$ProjectEndpoint,

    [Parameter(ParameterSetName = 'Set')]
    [string]$ModelDeploymentName,

    [Parameter(ParameterSetName = 'Set')]
    [string]$AppInsightsConnectionString,

    [Parameter(ParameterSetName = 'Set')]
    [string]$OpenAIEndpoint,

    [Parameter(ParameterSetName = 'Set')]
    [string]$ConfigFile,

    [Parameter(ParameterSetName = 'Set')]
    [switch]$Persist,

    [Parameter(ParameterSetName = 'Show')]
    [switch]$Show,

    [Parameter(ParameterSetName = 'Clear')]
    [switch]$Clear
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

#region Configuration

# Environment variable names
$EnvVars = @{
    ProjectEndpoint             = "PROJECT_ENDPOINT"
    ModelDeploymentName         = "MODEL_DEPLOYMENT_NAME"
    AppInsightsConnectionString = "APPLICATIONINSIGHTS_CONNECTION_STRING"
    OpenAIEndpoint              = "AZURE_OPENAI_ENDPOINT"
    # Optional/Additional
    TenantId                    = "AZURE_TENANT_ID"
    ClientId                    = "AZURE_CLIENT_ID"
    FabricWorkspaceId           = "779e4068-f8d3-4c2d-98ef-d463fdc351b5"
}

# Default values (update these with your deployment values)
$Defaults = @{
    ProjectEndpoint             = ""  # e.g., "https://eastus2.api.azureml.ms/agents/v1.0/subscriptions/..."
    ModelDeploymentName         = "gpt-4o-deployment"
    AppInsightsConnectionString = ""  # e.g., "InstrumentationKey=xxx;IngestionEndpoint=https://..."
    OpenAIEndpoint              = ""  # e.g., "https://your-openai.openai.azure.com/"
}

#endregion

#region Helper Functions

function Write-Banner {
    Write-Host ""
    Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║       Fabric Data Agent Router - Environment Setup         ║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
}

function Write-EnvVar {
    param(
        [string]$Name,
        [string]$Value,
        [switch]$Masked
    )
    
    $displayValue = if ($Masked -and $Value) {
        $len = $Value.Length
        if ($len -gt 20) {
            $Value.Substring(0, 10) + "..." + $Value.Substring($len - 5)
        }
        else {
            "***"
        }
    }
    else {
        if ([string]::IsNullOrEmpty($Value)) { "(not set)" } else { $Value }
    }
    
    $status = if ([string]::IsNullOrEmpty($Value)) { "⚠" } else { "✓" }
    $color = if ([string]::IsNullOrEmpty($Value)) { "Yellow" } else { "Green" }
    
    Write-Host "  $status " -ForegroundColor $color -NoNewline
    Write-Host "$Name" -ForegroundColor White -NoNewline
    Write-Host " = " -ForegroundColor DarkGray -NoNewline
    Write-Host $displayValue -ForegroundColor Gray
}

function Get-ConfigFromJson {
    param([string]$Path)
    
    if (-not (Test-Path $Path)) {
        Write-Warning "Config file not found: $Path"
        return @{}
    }
    
    $json = Get-Content -Path $Path -Raw | ConvertFrom-Json
    $config = @{}
    
    # Try different JSON structures
    if ($json.AzureAI) {
        if ($json.AzureAI.ProjectEndpoint) { $config.ProjectEndpoint = $json.AzureAI.ProjectEndpoint }
        if ($json.AzureAI.ModelDeploymentName) { $config.ModelDeploymentName = $json.AzureAI.ModelDeploymentName }
        if ($json.AzureAI.Endpoint) { $config.OpenAIEndpoint = $json.AzureAI.Endpoint }
    }
    
    if ($json.ApplicationInsights.ConnectionString) {
        $config.AppInsightsConnectionString = $json.ApplicationInsights.ConnectionString
    }
    
    # Direct properties
    if ($json.PROJECT_ENDPOINT) { $config.ProjectEndpoint = $json.PROJECT_ENDPOINT }
    if ($json.MODEL_DEPLOYMENT_NAME) { $config.ModelDeploymentName = $json.MODEL_DEPLOYMENT_NAME }
    
    return $config
}

function Get-ConfigFromEnvFile {
    param([string]$Path)
    
    if (-not (Test-Path $Path)) {
        Write-Warning "Env file not found: $Path"
        return @{}
    }
    
    $config = @{}
    Get-Content -Path $Path | ForEach-Object {
        if ($_ -match '^\s*([^#][^=]+)=(.*)$') {
            $key = $matches[1].Trim()
            $value = $matches[2].Trim().Trim('"', "'")
            
            switch ($key) {
                $EnvVars.ProjectEndpoint { $config.ProjectEndpoint = $value }
                $EnvVars.ModelDeploymentName { $config.ModelDeploymentName = $value }
                $EnvVars.AppInsightsConnectionString { $config.AppInsightsConnectionString = $value }
                $EnvVars.OpenAIEndpoint { $config.OpenAIEndpoint = $value }
            }
        }
    }
    
    return $config
}

function Set-EnvironmentVariable {
    param(
        [string]$Name,
        [string]$Value,
        [switch]$Persist
    )
    
    # Set for current session
    [Environment]::SetEnvironmentVariable($Name, $Value, [EnvironmentVariableTarget]::Process)
    
    # Persist to user profile if requested
    if ($Persist) {
        [Environment]::SetEnvironmentVariable($Name, $Value, [EnvironmentVariableTarget]::User)
    }
}

function Clear-EnvironmentVariable {
    param(
        [string]$Name,
        [switch]$Persist
    )
    
    [Environment]::SetEnvironmentVariable($Name, $null, [EnvironmentVariableTarget]::Process)
    
    if ($Persist) {
        [Environment]::SetEnvironmentVariable($Name, $null, [EnvironmentVariableTarget]::User)
    }
}

function Read-ConfigValue {
    param(
        [string]$Prompt,
        [string]$Default,
        [string]$CurrentValue,
        [switch]$Required
    )
    
    $displayDefault = if ($CurrentValue) { $CurrentValue } elseif ($Default) { $Default } else { "" }
    $promptText = if ($displayDefault) { "$Prompt [$displayDefault]" } else { $Prompt }
    
    Write-Host "  $promptText`: " -ForegroundColor Cyan -NoNewline
    $input = Read-Host
    
    $result = if ($input) { $input } elseif ($CurrentValue) { $CurrentValue } else { $Default }
    
    if ($Required -and [string]::IsNullOrEmpty($result)) {
        Write-Warning "This value is required!"
        return Read-ConfigValue -Prompt $Prompt -Default $Default -CurrentValue $CurrentValue -Required
    }
    
    return $result
}

function Test-AzureCliLogin {
    try {
        $account = az account show 2>$null | ConvertFrom-Json
        return $null -ne $account
    }
    catch {
        return $false
    }
}

function Get-AzureResourceInfo {
    Write-Host "`n  Fetching Azure resource information..." -ForegroundColor Gray
    
    $info = @{}
    
    try {
        # Get subscription info
        $account = az account show 2>$null | ConvertFrom-Json
        if ($account) {
            $info.SubscriptionId = $account.id
            $info.TenantId = $account.tenantId
            Write-Host "  ✓ Subscription: $($account.name)" -ForegroundColor Green
        }
        
        # List AI Projects
        $projects = az ml workspace list --query "[?kind=='Project']" 2>$null | ConvertFrom-Json
        if ($projects -and $projects.Count -gt 0) {
            Write-Host "`n  Available AI Projects:" -ForegroundColor Cyan
            for ($i = 0; $i -lt $projects.Count; $i++) {
                Write-Host "    [$($i + 1)] $($projects[$i].name) ($($projects[$i].location))" -ForegroundColor White
            }
        }
        
        # List OpenAI deployments
        $openaiAccounts = az cognitiveservices account list --query "[?kind=='OpenAI']" 2>$null | ConvertFrom-Json
        if ($openaiAccounts -and $openaiAccounts.Count -gt 0) {
            Write-Host "`n  Available OpenAI Services:" -ForegroundColor Cyan
            foreach ($account in $openaiAccounts) {
                Write-Host "    • $($account.name) - $($account.properties.endpoint)" -ForegroundColor White
            }
        }
    }
    catch {
        Write-Host "  ⚠ Could not fetch Azure resources (ensure az CLI is logged in)" -ForegroundColor Yellow
    }
    
    return $info
}

#endregion

#region Main Script

Write-Banner

# Handle Show mode
if ($Show) {
    Write-Host "  Current Environment Configuration:" -ForegroundColor White
    Write-Host "  ─────────────────────────────────────────────────────" -ForegroundColor DarkGray
    
    Write-EnvVar -Name $EnvVars.ProjectEndpoint -Value $env:PROJECT_ENDPOINT
    Write-EnvVar -Name $EnvVars.ModelDeploymentName -Value $env:MODEL_DEPLOYMENT_NAME
    Write-EnvVar -Name $EnvVars.AppInsightsConnectionString -Value $env:APPLICATIONINSIGHTS_CONNECTION_STRING -Masked
    Write-EnvVar -Name $EnvVars.OpenAIEndpoint -Value $env:AZURE_OPENAI_ENDPOINT
    Write-EnvVar -Name $EnvVars.TenantId -Value $env:AZURE_TENANT_ID
    Write-EnvVar -Name $EnvVars.ClientId -Value $env:AZURE_CLIENT_ID
    
    Write-Host ""
    
    # Validation
    $missing = @()
    if (-not $env:PROJECT_ENDPOINT) { $missing += "PROJECT_ENDPOINT" }
    if (-not $env:MODEL_DEPLOYMENT_NAME) { $missing += "MODEL_DEPLOYMENT_NAME" }
    
    if ($missing.Count -gt 0) {
        Write-Host "  ⚠ Missing required variables: $($missing -join ', ')" -ForegroundColor Yellow
        Write-Host "  Run '.\Set-DevEnvironment.ps1' to configure" -ForegroundColor Gray
    }
    else {
        Write-Host "  ✓ All required variables are set" -ForegroundColor Green
    }
    
    Write-Host ""
    exit 0
}

# Handle Clear mode
if ($Clear) {
    Write-Host "  Clearing environment variables..." -ForegroundColor Yellow
    
    $EnvVars.Values | ForEach-Object {
        Clear-EnvironmentVariable -Name $_ -Persist:$Persist
        Write-Host "  ✓ Cleared: $_" -ForegroundColor Gray
    }
    
    if ($Persist) {
        Write-Host "`n  ✓ Variables cleared from user profile" -ForegroundColor Green
    }
    else {
        Write-Host "`n  ✓ Variables cleared for current session" -ForegroundColor Green
    }
    
    Write-Host ""
    exit 0
}

# Set mode - gather configuration
$config = @{
    ProjectEndpoint             = $ProjectEndpoint
    ModelDeploymentName         = $ModelDeploymentName
    AppInsightsConnectionString = $AppInsightsConnectionString
    OpenAIEndpoint              = $OpenAIEndpoint
}

# Load from config file if specified
if ($ConfigFile) {
    Write-Host "  Loading configuration from: $ConfigFile" -ForegroundColor Cyan
    
    $extension = [System.IO.Path]::GetExtension($ConfigFile).ToLower()
    $fileConfig = switch ($extension) {
        ".json" { Get-ConfigFromJson -Path $ConfigFile }
        ".env" { Get-ConfigFromEnvFile -Path $ConfigFile }
        default { 
            Write-Warning "Unknown config file type: $extension"
            @{}
        }
    }
    
    # Merge file config (only for empty values)
    $fileConfig.GetEnumerator() | ForEach-Object {
        if ([string]::IsNullOrEmpty($config[$_.Key])) {
            $config[$_.Key] = $_.Value
        }
    }
}

# Try to load from existing appsettings files
$appSettingsPaths = @(
    ".\appsettings.Generated.json",
    ".\appsettings.Development.json",
    ".\appsettings.json",
    ".\src\FabricDataAgentRouter\appsettings.Generated.json",
    ".\src\FabricDataAgentRouter\appsettings.Development.json"
)

foreach ($path in $appSettingsPaths) {
    if (Test-Path $path) {
        Write-Host "  Found config file: $path" -ForegroundColor Gray
        $fileConfig = Get-ConfigFromJson -Path $path
        $fileConfig.GetEnumerator() | ForEach-Object {
            if ([string]::IsNullOrEmpty($config[$_.Key])) {
                $config[$_.Key] = $_.Value
            }
        }
        break
    }
}

# Fall back to current environment variables
if ([string]::IsNullOrEmpty($config.ProjectEndpoint)) { $config.ProjectEndpoint = $env:PROJECT_ENDPOINT }
if ([string]::IsNullOrEmpty($config.ModelDeploymentName)) { $config.ModelDeploymentName = $env:MODEL_DEPLOYMENT_NAME }
if ([string]::IsNullOrEmpty($config.AppInsightsConnectionString)) { $config.AppInsightsConnectionString = $env:APPLICATIONINSIGHTS_CONNECTION_STRING }
if ([string]::IsNullOrEmpty($config.OpenAIEndpoint)) { $config.OpenAIEndpoint = $env:AZURE_OPENAI_ENDPOINT }

# Fall back to defaults
if ([string]::IsNullOrEmpty($config.ModelDeploymentName)) { $config.ModelDeploymentName = $Defaults.ModelDeploymentName }

# Check if Azure CLI is available for resource discovery
$hasAzCli = Get-Command az -ErrorAction SilentlyContinue
if ($hasAzCli -and (Test-AzureCliLogin)) {
    $azInfo = Get-AzureResourceInfo
}

# Interactive prompts for missing required values
Write-Host "`n  Configuration (press Enter to keep current value):" -ForegroundColor White
Write-Host "  ─────────────────────────────────────────────────────" -ForegroundColor DarkGray

$config.ProjectEndpoint = Read-ConfigValue `
    -Prompt "Project Endpoint" `
    -CurrentValue $config.ProjectEndpoint `
    -Required

$config.ModelDeploymentName = Read-ConfigValue `
    -Prompt "Model Deployment Name" `
    -CurrentValue $config.ModelDeploymentName `
    -Default "gpt-4o-deployment" `
    -Required

$config.OpenAIEndpoint = Read-ConfigValue `
    -Prompt "OpenAI Endpoint (optional)" `
    -CurrentValue $config.OpenAIEndpoint

$config.AppInsightsConnectionString = Read-ConfigValue `
    -Prompt "App Insights Connection String (optional)" `
    -CurrentValue $config.AppInsightsConnectionString

# Set environment variables
Write-Host "`n  Setting environment variables..." -ForegroundColor Cyan

Set-EnvironmentVariable -Name $EnvVars.ProjectEndpoint -Value $config.ProjectEndpoint -Persist:$Persist
Write-Host "  ✓ $($EnvVars.ProjectEndpoint)" -ForegroundColor Green

Set-EnvironmentVariable -Name $EnvVars.ModelDeploymentName -Value $config.ModelDeploymentName -Persist:$Persist
Write-Host "  ✓ $($EnvVars.ModelDeploymentName)" -ForegroundColor Green

if ($config.OpenAIEndpoint) {
    Set-EnvironmentVariable -Name $EnvVars.OpenAIEndpoint -Value $config.OpenAIEndpoint -Persist:$Persist
    Write-Host "  ✓ $($EnvVars.OpenAIEndpoint)" -ForegroundColor Green
}

if ($config.AppInsightsConnectionString) {
    Set-EnvironmentVariable -Name $EnvVars.AppInsightsConnectionString -Value $config.AppInsightsConnectionString -Persist:$Persist
    Write-Host "  ✓ $($EnvVars.AppInsightsConnectionString)" -ForegroundColor Green
}

# Summary
Write-Host ""
Write-Host "  ─────────────────────────────────────────────────────" -ForegroundColor DarkGray

if ($Persist) {
    Write-Host "  ✓ Environment variables saved to user profile" -ForegroundColor Green
    Write-Host "    (Will persist across terminal sessions)" -ForegroundColor Gray
}
else {
    Write-Host "  ✓ Environment variables set for current session" -ForegroundColor Green
    Write-Host "    (Use -Persist to save permanently)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "  To verify configuration:" -ForegroundColor White
Write-Host "    .\Set-DevEnvironment.ps1 -Show" -ForegroundColor Gray
Write-Host ""
Write-Host "  To run the application:" -ForegroundColor White
Write-Host "    cd src\FabricDataAgentRouter" -ForegroundColor Gray
Write-Host "    dotnet run" -ForegroundColor Gray
Write-Host ""

#endregion

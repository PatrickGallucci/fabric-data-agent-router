<#
.SYNOPSIS
    Deploys Azure AI Foundry infrastructure for the Fabric Data Agent Router.

.DESCRIPTION
    This script creates:
    - Resource Group (if not exists)
    - Azure AI Foundry resource (AIServices kind)
    - Foundry Project
    - Model deployment (GPT-4o for routing)
    - Key Vault for secrets
    - Application Insights for monitoring

.PARAMETER SubscriptionId
    Azure subscription ID

.PARAMETER ResourceGroupName
    Name of the resource group to create/use

.PARAMETER Location
    Azure region (must support AI Foundry)

.PARAMETER ProjectName
    Name of the Foundry project

.EXAMPLE
    .\Deploy-FoundryInfra.ps1 -SubscriptionId "xxx" -ResourceGroupName "rg-foundry" -Location "eastus2" -ProjectName "fabric-router"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$TenantId = "3ae449e7-25e5-4e5d-b705-7a39e1ad16f0",

    [Parameter(Mandatory = $false)]
    [string]$SubscriptionId = "323241e8-df5e-434e-b1d4-a45c3576bf80",

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName = "rg-dataagents-eus2",

    [Parameter(Mandatory = $false)]
    [string]$Location = "eastus2",

    [Parameter(Mandatory = $false)]
    [string]$ProjectName = "fabric-router",
    
    [Parameter(Mandatory = $false)]
    [string]$FoundaryResourceName = "foundagents",

    [Parameter(Mandatory = $false)]
    [string]$KeyVaultResoureName = "kvagents",

    [Parameter(Mandatory = $false)]
    [string]$ModelName = "gpt-4o",

    [Parameter(Mandatory = $false)]
    [string]$ModelVersion = "2024-11-20"
)

#region Functions

function Write-Step {
    param([string]$Message)
    Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━`n" -ForegroundColor Cyan
}

function Test-AzureLogin {
    try {
        $context = Get-AzContext
        if (-not $context) {
            Write-Host "Not logged in. Please authenticate..." -ForegroundColor Yellow
            Connect-AzAccount -Tenant $TenantId 
        }
        return $true
    }
    catch {
        Write-Error "Failed to verify Azure login: $_"
        return $false
    }
}

function Get-UniqueResourceName {
    param(
        [string]$Prefix,
        [string]$Suffix = ""
    )
    $uniqueId = (Get-Random -Minimum 1000 -Maximum 9999).ToString()
    $name = "$Prefix$uniqueId$Suffix".ToLower() -replace '[^a-z0-9]', ''
    return $name.Substring(0, [Math]::Min($name.Length, 24))
}

function New-UniqueResourceName {
    param([string]$Prefix, [int]$Length = 8)
    $suffix = -join ((97..122) | Get-Random -Count $Length | ForEach-Object { [char]$_ })
    return "$Prefix$suffix"
}

#endregion

#region Main Script

Write-Host @"

╔═══════════════════════════════════════════════════════════════════════════════╗
║         AZURE AI FOUNDRY INFRASTRUCTURE DEPLOYMENT                            ║
║         Fabric Data Agent Router Project                                      ║
╚═══════════════════════════════════════════════════════════════════════════════╝

"@ -ForegroundColor Green

az config set core.login_experience_v2=off
az login --tenant $TenantId
az account set --subscription $SubscriptionId 

 $uniqueSuffix = Get-Random -Minimum 100 -Maximum 999

# Verify Azure login
Write-Step "Verifying Azure Authentication"
if (-not (Test-AzureLogin)) {
    exit 1
}

# Set subscription context
Write-Step "Setting Subscription Context"
try {
    Connect-AzAccount -Tenant $TenantId | Out-Null
    Set-AzContext -SubscriptionId $SubscriptionId | Out-Null
    Write-Host " Subscription set: $SubscriptionId" -ForegroundColor Green
}
catch {
    Write-Error "Failed to set subscription: $_"
    exit 1
}

# Create resource group if not exists
Write-Step "Creating Resource Group"
$rg = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction SilentlyContinue
if (-not $rg) {
    $rg = New-AzResourceGroup -Name $ResourceGroupName -Location $Location
    Write-Host " Created resource group: $ResourceGroupName" -ForegroundColor Green
}
else {
    Write-Host " Resource group exists: $ResourceGroupName" -ForegroundColor Yellow
}

# Generate unique names
$foundryAccountName = "$FoundaryResourceName{0}" -f $uniqueSuffix
$keyVaultName = "$KeyVaultResoureName{0}" -f $uniqueSuffix
$appInsightsName = "$ProjectName-insights"
$logAnalyticsName = "$ProjectName-logs"

# Create Log Analytics Workspace
Write-Step "Creating Log Analytics Workspace"
$logAnalytics = Get-AzOperationalInsightsWorkspace -ResourceGroupName $ResourceGroupName -Name $logAnalyticsName -ErrorAction SilentlyContinue
if (-not $logAnalytics) {
    $logAnalytics = New-AzOperationalInsightsWorkspace `
        -ResourceGroupName $ResourceGroupName `
        -Name $logAnalyticsName `
        -Location $Location `
        -Sku PerGB2018
    Write-Host " Created Log Analytics workspace: $logAnalyticsName" -ForegroundColor Green
}
else {
    Write-Host " Log Analytics workspace exists: $logAnalyticsName" -ForegroundColor Yellow
}

# Create Application Insights
Write-Step "Creating Application Insights"
$appInsights = Get-AzApplicationInsights -ResourceGroupName $ResourceGroupName -Name $appInsightsName -ErrorAction SilentlyContinue
if (-not $appInsights) {
    $appInsights = New-AzApplicationInsights `
        -ResourceGroupName $ResourceGroupName `
        -Name $appInsightsName `
        -Location $Location `
        -WorkspaceResourceId $logAnalytics.ResourceId `
        -Kind web
    Write-Host " Created Application Insights: $appInsightsName" -ForegroundColor Green
}
else {
    Write-Host " Application Insights exists: $appInsightsName" -ForegroundColor Yellow
}

# Create Key Vault
Write-Step "Creating Key Vault"
$keyVault = Get-AzKeyVault -VaultName $keyVaultName -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue
if (-not $keyVault) {
    $keyVault = New-AzKeyVault `
        -VaultName $keyVaultName `
        -ResourceGroupName $ResourceGroupName `
        -Location $Location
    Write-Host " Created Key Vault: $keyVaultName" -ForegroundColor Green
}
else {
    Write-Host " Key Vault exists: $keyVaultName" -ForegroundColor Yellow
}

# Create Azure AI Foundry Account (AIServices)
Write-Step "Creating Azure AI Foundry Resource"

# Create Azure AI Foundry Account (AIServices)
Write-Step "Creating Azure AI Foundry Resource"


try {
    $deployment = New-AzResourceGroupDeployment `
        -ResourceGroupName $ResourceGroupName `
        -TemplateFile $templateFile `
        -accountName $foundryAccountName `
        -location $Location `
        -projectName $ProjectName `
        -Verbose

    Write-Host " Created Foundry resource: $foundryAccountName" -ForegroundColor Green
    Write-Host " Created Foundry project: $ProjectName" -ForegroundColor Green
}
catch {
    Write-Warning "ARM deployment encountered an issue: $_"
    Write-Host "Attempting alternative deployment via Azure CLI..." -ForegroundColor Yellow
    
    # Fallback to Azure CLI
    az cognitiveservices account create `
        --name $foundryAccountName `
        --resource-group $ResourceGroupName `
        --kind AIServices `
        --sku S0 `
        --location $Location `
        --custom-domain $foundryAccountName `
        --assign-identity
}

az cognitiveservices account project create --location $Location `
                                            --name $foundryAccountName `
                                            --project-name $ProjectName `
                                            --resource-group $ResourceGroupName `
                                            --assign-identity `
                                            --description "Foundry Project for Fabric Data Agent Router" `
                                            --display-name "Fabric Data Agent Router Project" 

# Get the Foundry endpoint
Write-Step "Retrieving Foundry Endpoint"
$foundryAccount = Get-AzCognitiveServicesAccount -ResourceGroupName $ResourceGroupName -Name $foundryAccountName -ErrorAction SilentlyContinue
if ($foundryAccount) {
    $endpoint = $foundryAccount.Endpoint
    Write-Host " Foundry Endpoint: $endpoint" -ForegroundColor Green
}

# Deploy model for routing
Write-Step "Deploying Model for Agent Routing"

$modelDeploymentName = "gpt-4o-router"

try {
    # Deploy model using Azure CLI (more reliable for model deployments)
    $modelDeployResult = az cognitiveservices account deployment create `
        --name $foundryAccountName `
        --resource-group $ResourceGroupName `
        --deployment-name $modelDeploymentName `
        --model-name $ModelName `
        --model-version $ModelVersion `
        --model-format OpenAI `
        --sku-capacity 10 `
        --sku-name Standard 

    if ($LASTEXITCODE -eq 0) {
        Write-Host " Deployed model: $ModelName ($modelDeploymentName)" -ForegroundColor Green
    }
    else {
        Write-Warning "Model deployment failed: $modelDeployResult"
    }
}
catch {
    Write-Warning "Model deployment failed: az cognitiveservices account deployment create `
        --name $foundryAccountName `
        --resource-group $ResourceGroupName `
        --deployment-name $modelDeploymentName `
        --model-name $ModelName `
        --model-version $ModelVersion `
        --model-format OpenAI `
        --sku-capacity 10 `
        --sku-name Standard "
}

# Store secrets in Key Vault
Write-Step "Storing Configuration in Key Vault"

$foundryKey = (Get-AzCognitiveServicesAccountKey -ResourceGroupName $ResourceGroupName -Name $foundryAccountName).Key1

Set-AzKeyVaultSecret -VaultName $keyVaultName -Name "FoundryApiKey" -SecretValue (ConvertTo-SecureString $foundryKey -AsPlainText -Force) | Out-Null
Set-AzKeyVaultSecret -VaultName $keyVaultName -Name "FoundryEndpoint" -SecretValue (ConvertTo-SecureString $endpoint -AsPlainText -Force) | Out-Null

Write-Host " Stored secrets in Key Vault" -ForegroundColor Green

# Output summary
Write-Step "Deployment Complete!"

$summary = @"

╔═══════════════════════════════════════════════════════════════════════════════╗
                          DEPLOYMENT SUMMARY                                   
╚═══════════════════════════════════════════════════════════════════════════════╝
                                                                               
  Resource Group:        $ResourceGroupName
  Location:              $Location
                                                                               
  Foundry Account:       $foundryAccountName
  Foundry Project:       $ProjectName
  Model Deployment:      $modelDeploymentName
                                                                               
  Key Vault:             $keyVaultName
  Application Insights:  $appInsightsName
                                                                               
╔═══════════════════════════════════════════════════════════════════════════════╗
  ENDPOINTS                                                                    
╚═══════════════════════════════════════════════════════════════════════════════╝
                                                                               
  Foundry Endpoint:                                                            
  $endpoint
                                                                               
  Project Endpoint:                                                            
  $endpoint/projects/$ProjectName
                                                                               
╔═══════════════════════════════════════════════════════════════════════════════╗
  NEXT STEPS                                                                   
╚═══════════════════════════════════════════════════════════════════════════════╝
                                                                               
  1. Configure fabric-agents.json with your Fabric Data Agent MCP URLs         
  2. Grant Azure AI User role to developers                                    
  3. Build and run the C# router application                                   
  4. Test with sample queries                                                  
                                                                               
╚═══════════════════════════════════════════════════════════════════════════════╝

"@

Write-Host $summary -ForegroundColor Cyan

# Export environment variables file
$envFile = @"
# Azure AI Foundry Configuration
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

AZURE_SUBSCRIPTION_ID=$SubscriptionId
AZURE_RESOURCE_GROUP=$ResourceGroupName
AZURE_AI_FOUNDRY_ACCOUNT=$foundryAccountName
AZURE_AI_FOUNDRY_PROJECT=$ProjectName
AZURE_AI_FOUNDRY_ENDPOINT=$endpoint
AZURE_KEYVAULT_NAME=$keyVaultName
AZURE_APPINSIGHTS_NAME=$appInsightsName
MODEL_DEPLOYMENT_NAME=$modelDeploymentName
"@

# Write to a file (overwrite)
$outFile = "$PSScriptRoot\azure-ai-foundry.env"
$envFile | Set-Content -Path $outFile -Encoding UTF8
[IO.File]::WriteAllText($outFile, $envFile, [Text.UTF8Encoding]::new($false))  

# Write to a file (overwrite)
$outFile = "$PSScriptRoot\azure-ai-foundry.txt"
$envFile | Set-Content -Path $outFile -Encoding UTF8
[IO.File]::WriteAllText($outFile, $envFile, [Text.UTF8Encoding]::new($false))  

Write-Host "Environment variables saved to: $outFile" -ForegroundColor Green

#endregion

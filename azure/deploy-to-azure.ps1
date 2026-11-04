<#
.SYNOPSIS
    Deploys the Patient Management .NET 10 API, Azure SQL Database, and Azure Blob Storage to Azure App Service.

.DESCRIPTION
    This script provisions Azure resources using Bicep/ARM, builds and packages the .NET 10 Web API,
    deploys the package to Azure App Service, and displays the public Swagger URL.

.PARAMETER ResourceGroupName
    Name of the Azure Resource Group. Default: 'rg-patient-management'

.PARAMETER Location
    Azure region for deployment. Default: 'eastus'

.PARAMETER SqlPassword
    Password for the Azure SQL Server administrator. If omitted, a strong password is generated.
#>

[CmdletBinding()]
param (
    [string]$ResourceGroupName = "rg-patient-management",
    [string]$Location = "eastus",
    [string]$SqlPassword = ""
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Patient Management API - Azure App Service Deployment" -ForegroundColor Cyan
Write-Host " Target Framework: .NET 10 | Swagger UI Enabled" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Verify Azure CLI is installed
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI (az) is not installed. Please install Azure CLI from https://aka.ms/installazurecliwindows before running this script."
    exit 1
}

# 2. Check Azure login
Write-Host "`n[1/5] Checking Azure authentication..." -ForegroundColor Yellow
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Not logged in. Initiating Azure login..." -ForegroundColor Yellow
    az login --output none
    $account = az account show | ConvertFrom-Json
}
Write-Host "Logged into Subscription: $($account.name) ($($account.id))" -ForegroundColor Green

# 3. Create Resource Group if not exists
Write-Host "`n[2/5] Creating or verifying Resource Group '$ResourceGroupName' in '$Location'..." -ForegroundColor Yellow
az group create --name $ResourceGroupName --location $Location --output none
Write-Host "Resource Group '$ResourceGroupName' ready." -ForegroundColor Green

# 4. Generate SQL Password if not provided
if ([string]::IsNullOrWhiteSpace($SqlPassword)) {
    $SqlPassword = "P@" + [Guid]::NewGuid().ToString("N").Substring(0, 14) + "!"
    Write-Host "Generated secure SQL Administrator Password." -ForegroundColor Gray
}

# 5. Provision Infrastructure via Bicep
Write-Host "`n[3/5] Provisioning App Service, Azure SQL, and Blob Storage via Bicep..." -ForegroundColor Yellow
$bicepPath = Join-Path $PSScriptRoot "deploy.bicep"
$deployment = az deployment group create `
    --resource-group $ResourceGroupName `
    --template-file $bicepPath `
    --parameters sqlAdminPassword="$SqlPassword" `
    --output json | ConvertFrom-Json

$webAppUrl = $deployment.properties.outputs.webAppUrl.value
$swaggerUrl = $deployment.properties.outputs.swaggerUrl.value
$webAppName = $webAppUrl -replace "https://", "" -replace "\.azurewebsites\.net.*", ""

Write-Host "Provisioned Web App: $webAppName" -ForegroundColor Green

# 6. Publish and Package .NET 10 API
Write-Host "`n[4/5] Building and publishing .NET 10 project..." -ForegroundColor Yellow
$projectDir = Resolve-Path (Join-Path $PSScriptRoot "..\src\PatientManagement.Api")
$publishDir = Join-Path $projectDir "bin\Release\net10.0\publish"
$zipPath = Join-Path $projectDir "bin\Release\net10.0\publish.zip"

dotnet publish "$projectDir\PatientManagement.Api.csproj" -c Release -o "$publishDir" /p:UseAppHost=false

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force
Write-Host "Packaged release artifact to: $zipPath" -ForegroundColor Green

# 7. Deploy to Azure App Service
Write-Host "`n[5/5] Deploying package to Azure App Service '$webAppName'..." -ForegroundColor Yellow
az webapp deploy --resource-group $ResourceGroupName --name $webAppName --src-path $zipPath --type zip --output none

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " Deployment Succeeded!" -ForegroundColor Green
Write-Host " Web API URL:     $webAppUrl" -ForegroundColor Cyan
Write-Host " Live Swagger UI: $swaggerUrl" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Green

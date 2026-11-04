#!/usr/bin/env bash
set -euo pipefail

# Patient Management .NET 10 API - Azure App Service Deployment Script
RESOURCE_GROUP="${1:-rg-patient-management}"
LOCATION="${2:-eastus}"
SQL_PASSWORD="${3:-P@$(openssl rand -hex 8)!}"

echo "=========================================================="
echo " Patient Management API - Azure App Service Deployment"
echo " Target Framework: .NET 10 | Swagger UI Enabled"
echo "=========================================================="

# Check Azure CLI
if ! command -v az &> /dev/null; then
    echo "Error: Azure CLI is required. Please install 'az' CLI."
    exit 1
fi

# Ensure logged in
echo "[1/5] Checking Azure login..."
az account show --output none || az login

# Create resource group
echo "[2/5] Creating Resource Group '$RESOURCE_GROUP' in '$LOCATION'..."
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

# Provision infrastructure
echo "[3/5] Provisioning Azure resources via Bicep..."
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOYMENT_JSON=$(az deployment group create \
    --resource-group "$RESOURCE_GROUP" \
    --template-file "$SCRIPT_DIR/deploy.bicep" \
    --parameters sqlAdminPassword="$SQL_PASSWORD" \
    --output json)

WEB_APP_URL=$(echo "$DEPLOYMENT_JSON" | grep -o '"webAppUrl": *{[^}]*"value": *"[^"]*"' | sed -E 's/.*"value": *"([^"]*)".*/\1/')
SWAGGER_URL=$(echo "$DEPLOYMENT_JSON" | grep -o '"swaggerUrl": *{[^}]*"value": *"[^"]*"' | sed -E 's/.*"value": *"([^"]*)".*/\1/')
WEB_APP_NAME=$(basename "$WEB_APP_URL" | cut -d. -f1 | sed 's/https:\/\///')

# Build and Publish
echo "[4/5] Building and packaging .NET 10 application..."
PUBLISH_DIR="$SCRIPT_DIR/../src/PatientManagement.Api/bin/Release/net10.0/publish"
ZIP_PATH="$SCRIPT_DIR/../src/PatientManagement.Api/bin/Release/net10.0/publish.zip"

dotnet publish "$SCRIPT_DIR/../src/PatientManagement.Api/PatientManagement.Api.csproj" -c Release -o "$PUBLISH_DIR" /p:UseAppHost=false
(cd "$PUBLISH_DIR" && zip -r "$ZIP_PATH" .)

# Deploy to Azure App Service
echo "[5/5] Deploying artifact to Azure App Service '$WEB_APP_NAME'..."
az webapp deploy --resource-group "$RESOURCE_GROUP" --name "$WEB_APP_NAME" --src-path "$ZIP_PATH" --type zip --output none

echo "=========================================================="
echo " Deployment Complete!"
echo " Web App URL: $WEB_APP_URL"
echo " Live Swagger UI: $SWAGGER_URL"
echo "=========================================================="

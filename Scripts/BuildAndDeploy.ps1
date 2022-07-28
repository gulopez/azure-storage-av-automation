param (
    [Parameter(Mandatory=$true)][string] $sourceCodeContainerName,
    [Parameter(Mandatory=$true)][string] $sourceCodeStorageAccountName,
    [Parameter(Mandatory=$true)][string] $targetContainerName,
    [Parameter(Mandatory=$true)][string] $targetStorageAccountName,
    [Parameter(Mandatory=$true)][string] $targetResourceGroup,
    [Parameter(Mandatory=$true)][string] $subscriptionID,
    [Parameter(Mandatory=$true)][string] $deploymentResourceGroupName,
    [Parameter(Mandatory=$true)][string] $deploymentResourceGroupLocation,
    [Parameter(Mandatory=$true)][string] $vmUserName,
    [Parameter(Mandatory=$true)][string] $vmPassword,
    $ArmTemplatFile = "$PSScriptRoot/../ARM_template/AntivirusAutomationForStorageTemplate.json"
)


$deploymentResourceGroupName ="defDemoRG9"
$deploymentResourceGroupLocation ="eastus"
$targetContainerName="new-files"
$targetStorageAccountName="stagingcxdefenderdemo"
$targetResourceGroup="cxdefenderstdemo"
$subscriptionID ="ca8af7e5-0c5e-4d5d-bdbf-07e2f1ba6ff0"


$SASdurationhours="2"
$StorageEndpointsuffix = "blob.core.windows.net"
$vmUserName ="gulopez"

$vmPassword = Read-Host -Prompt "Enter password" -AsSecureString 
#$vmPassword = ConvertTo-SecureString $vmPassword -AsPlainText -Force

$sourceCodeContainerName ="source"
$sourceCodeStorageAccountName = "defendersourcestacc"


#cd C:\Gustavo\Projects\Repos\azure-storage-av-automation-gulopez\Scripts

$scriptRoot = Get-Location


$ArmTemplatFile = "$scriptRoot/../ARM_template/AntivirusAutomationForStorageTemplate.json"








#$relativePath = Get-Item "$scriptRoot\..\ScanHttpServer" | Resolve-Path -Relative
#$ScanHttpServerRoot = $relativePath 

$ScanHttpServerRoot = "..\ScanHttpServer"


$ScanHttpServerZipPath = "$ScanHttpServerRoot\ScanHttpServer.Zip"
$VMInitScriptPath = "$ScanHttpServerRoot\VMInit.ps1"
$ScanUploadedBlobRoot = "..\ScanUploadedBlobFunction"
$ScanUploadedBlobZipPath = "$ScanUploadedBlobRoot\ScanUploadedBlobFunction.zip"

#az cloud set --name AzureUSGovernment
#az account list-locations

#az cloud list
#az cloud set --name AzureUSGovernment
az cloud set --name AzureCloud



az login

#Build and Zip ScanHttpServer code 
Write-Host Build ScanHttpServer
dotnet publish $ScanHttpServerRoot\ScanHttpServer.csproj -c Release -o $ScanHttpServerRoot/out

Write-Host Zip ScanHttpServer
$compress = @{
    Path            = "$ScanHttpServerRoot\out\*", "$ScanHttpServerRoot\runLoop.ps1",  "$ScanHttpServerRoot\azcopy.exe"
    DestinationPath = $ScanHttpServerZipPath
}
Compress-Archive @compress -Update
Write-Host ScanHttpServer zipped successfully

# Build and Zip ScanUploadedBlob Function
Write-Host Build ScanUploadedBlob
dotnet publish $ScanUploadedBlobRoot\ScanUploadedBlobFunction.csproj -c Release -o $ScanUploadedBlobRoot\out

Write-Host Zip ScanUploadedBlob code
Compress-Archive -Path $ScanUploadedBlobRoot\out\* -DestinationPath $ScanUploadedBlobZipPath -Update
Write-Host ScanUploadedBlob zipped successfully

# Uploading ScanHttpServer code 
Write-Host Uploading Files
Write-Host Creating container if not exists
az storage container create `
    --name $sourceCodeContainerName `
    --account-name $sourceCodeStorageAccountName `
    --subscription $subscriptionID `
    --public-access blob

$ScanHttpServerBlobName = "ScanHttpServer.zip"
az storage blob upload `
    --file $ScanHttpServerZipPath `
    --name $ScanHttpServerBlobName `
    --container-name $sourceCodeContainerName `
    --account-name $sourceCodeStorageAccountName `
    --subscription $subscriptionID

$ScanHttpServerUrl = az storage blob url `
    --name $ScanHttpServerBlobName `
    --container-name $sourceCodeContainerName `
    --account-name $sourceCodeStorageAccountName `
    --subscription $subscriptionID `

$ScanUploadedBlobFubctionBlobName = "ScanUploadedBlobFunction.zip"
az storage blob upload `
    --file $ScanUploadedBlobZipPath `
    --name $ScanUploadedBlobFubctionBlobName `
    --container-name $sourceCodeContainerName `
    --account-name $sourceCodeStorageAccountName `
    --subscription $subscriptionID 

$ScanUploadedBlobFubctionUrl = az storage blob url `
    --name $ScanUploadedBlobFubctionBlobName `
    --container-name $sourceCodeContainerName `
    --account-name $sourceCodeStorageAccountName `
    --subscription $subscriptionID 

$VMInitScriptBlobName = "VMInit.ps1"
az storage blob upload `
    --file $VMInitScriptPath `
    --name $VMInitScriptBlobName `
    --container-name $sourceCodeContainerName `
    --account-name $sourceCodeStorageAccountName `
    --subscription $subscriptionID
    
$VMInitScriptUrl = az storage blob url `
    --name $VMInitScriptBlobName `
    --container-name $sourceCodeContainerName `
    --account-name $sourceCodeStorageAccountName `
    --subscription $subscriptionID



$ScanHttpServerUrl="https://github.com/gulopez/azure-storage-av-automation/releases/download/1.0/ScanHttpServer.Zip"
$ScanUploadedBlobFubctionUrl="https://github.com/gulopez/azure-storage-av-automation/releases/download/1.0/ScanUploadedBlobFunction.zip"
$VMInitScriptUrl = "https://github.com/gulopez/azure-storage-av-automation/releases/download/1.0/VMInit.ps1"


Write-Host $ScanHttpServerUrl
Write-Host $ScanUploadedBlobFubctionUrl
Write-Host $VMInitScriptUrl

az group create `
    --location $deploymentResourceGroupLocation `
    --name $deploymentResourceGroupName `
    --subscription $subscriptionID

az deployment group create `
    --subscription $subscriptionID `
    --name "AntivirusAutomationForStorageTemplate" `
    --resource-group $deploymentResourceGroupName `
    --template-file $ArmTemplatFile `
    --parameters ScanHttpServerZipURL=$ScanHttpServerUrl `
    --parameters ScanUploadedBlobFunctionZipURL=$ScanUploadedBlobFubctionUrl `
    --parameters VMInitScriptURL=$VMInitScriptUrl `
    --parameters NameOfTargetContainer=$targetContainerName `
    --parameters NameOfTargetStorageAccount=$targetStorageAccountName `
    --parameters NameOfTheResourceGroupTheTargetStorageAccountBelongsTo=$targetResourceGroup `
    --parameters SubscriptionIDOfTheTargetStorageAccount=$subscriptionID `
    --parameters VMAdminUsername=$vmUserName `
    --parameters VMAdminPassword=$vmPassword  `
    --parameters StorageEndpointsuffix=$StorageEndpointsuffix  `
    --parameters SASdurationhours=$SASdurationhours




 #   az group delete --name $deploymentResourceGroupName --no-wait --yes
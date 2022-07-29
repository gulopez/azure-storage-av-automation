#az cloud set --name AzureUSGovernment
#az account list-locations

#az cloud list
az cloud set --name AzureUSGovernment
#az cloud set --name AzureCloud

az login

#########################################################Creating Source Storage account############################################################
$targetResourceGroup="defdemoRG"
$targetStorageAccountName="stagdefdemo"


#az account list-locations -o table
$deploymentResourceGroupLocation ="usgovvirginia"
$targetContainerName="new-files"
$targetCleanContainerName="clean-files"
$targetQuarantineContainerName="quarantine-files"

#$subscriptionID ="ca8af7e5-0c5e-4d5d-bdbf-07e2f1ba6ff0"
$subscriptionID ="4906dee5-e195-4bbe-81c4-ffdd264e1dfd"

az account set --subscription $subscriptionID
az account show

az group create `
    --location $deploymentResourceGroupLocation `
    --name $targetResourceGroup `
    --subscription $subscriptionID

az storage account create -n $targetStorageAccountName -g $targetResourceGroup -l $deploymentResourceGroupLocation --sku Standard_LRS


az storage container create -n $targetContainerName --account-name $targetStorageAccountName  --public-access blob
az storage container create -n $targetCleanContainerName --account-name $targetStorageAccountName  
az storage container create -n $targetQuarantineContainerName --account-name $targetStorageAccountName  


###############################################################################################


# Change current path of this script location
CD "C:\Gustavo\Projects\Repos\azure-storage-av-automation-gulopez\Scripts"
$scriptRoot = Get-Location





$scriptRoot = Get-Location


#################### Build Solution ################################

$ScanHttpServerRoot = "..\ScanHttpServer"
$ScanHttpServerZipPath = "$ScanHttpServerRoot\ScanHttpServer.Zip"
$VMInitScriptPath = "$ScanHttpServerRoot\VMInit.ps1"
$ScanUploadedBlobRoot = "..\ScanUploadedBlobFunction"
$ScanUploadedBlobZipPath = "$ScanUploadedBlobRoot\ScanUploadedBlobFunction.zip"


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



##########################  Upload Package to Storage Account ##############################


$sourceCodeContainerName ="source"
$sourceCodeStorageAccountName = "defendersourcestacc"


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

Write-Host $ScanHttpServerUrl
Write-Host $ScanUploadedBlobFubctionUrl
Write-Host $VMInitScriptUrl

###################################################################

#############  Hardcoding the package links



$ScanHttpServerUrl="https://github.com/gulopez/azure-storage-av-automation/releases/download/1.0/ScanHttpServer.Zip"
$ScanUploadedBlobFubctionUrl="https://github.com/gulopez/azure-storage-av-automation/releases/download/1.0/ScanUploadedBlobFunction.zip"
$VMInitScriptUrl = "https://github.com/gulopez/azure-storage-av-automation/releases/download/1.0/VMInit.ps1"


#########################################################################


$ArmTemplatFile = "$scriptRoot/../ARM_template/AntivirusAutomationForStorageTemplate.json"

#$relativePath = Get-Item "$scriptRoot\..\ScanHttpServer" | Resolve-Path -Relative
#$ScanHttpServerRoot = $relativePath 

$deploymentResourceGroupName ="defDemoRG2"
$SASdurationhours="1"

# for public cloud use blob.core.windows.net  for UsGov use blob.core.usgovcloudapi.net
$StorageEndpointsuffix = "blob.core.usgovcloudapi.net"
$vmUserName ="gulopez"
$vmPassword = Read-Host -Prompt "Enter password" -AsSecureString 

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




az group delete --name $deploymentResourceGroupName --no-wait --yes
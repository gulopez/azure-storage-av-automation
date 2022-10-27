using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Threading.Tasks;

namespace ScanUploadedBlobFunction
{
    public class Remediation
    {
        private ScanResults scanResults { get; }
        private ILogger log { get; }
        public Remediation(ScanResults scanResults, ILogger log)
        {
            this.scanResults = scanResults;
            this.log = log;
        }

        public void Start()
        {
            string srcContainerName = Environment.GetEnvironmentVariable(ScanConstants.SOURCE_CONTAINER_NAME);

            if (scanResults.isThreat)
            {
                log.LogInformation($"A malicious file was detected, file name: {scanResults.fileName}, threat type: {scanResults.threatType}");
                try
                {
                    string malwareContainerName = Environment.GetEnvironmentVariable(ScanConstants.MALWARE_CONTAINER_NAME);
                    MoveBlob(scanResults.fileName, srcContainerName, malwareContainerName, log).GetAwaiter().GetResult();
                    log.LogInformation("A malicious file was detected. It has been moved from the unscanned container to the quarantine container");
                }

                catch (Exception e)
                {
                    log.LogError($"A malicious file was detected, but moving it to the quarantine storage container failed. {e.Message}");
                }
            }

            else
            {
                try
                {
                    string cleanContainerName = Environment.GetEnvironmentVariable(ScanConstants.CLEAN_CONTAINER_NAME);
                    MoveBlob(scanResults.fileName, srcContainerName, cleanContainerName, log).GetAwaiter().GetResult();
                    log.LogInformation("The file is clean. It has been moved from the unscanned container to the clean container");
                }

                catch (Exception e)
                {
                    log.LogError($"The file is clean, but moving it to the clean storage container failed. {e.Message}");
                }
            }
        }

        private static async Task MoveBlob(string srcBlobName, string srcContainerName, string destContainerName, ILogger log)
        {
            //Note: if the srcBlob name already exist in the dest container it will be overwritten
            
            var sourceconnectionString = Environment.GetEnvironmentVariable(ScanConstants.SOURCE_DEFENDER_STORAGE);
            var targetconnectionString = Environment.GetEnvironmentVariable(ScanConstants.TARGET_DEFENDER_STORAGE);

            var srcContainer = new BlobContainerClient(sourceconnectionString, srcContainerName);


            var destContainer = new BlobContainerClient(targetconnectionString, destContainerName);
            destContainer.CreateIfNotExists();

            var srcBlob = srcContainer.GetBlobClient(srcBlobName);
            var destBlob = destContainer.GetBlobClient(srcBlobName);

            if (await srcBlob.ExistsAsync())
            {
                log.LogInformation("MoveBlob: Started file copy");
                await destBlob.StartCopyFromUriAsync(srcBlob.Uri);
                log.LogInformation("MoveBlob: Done file copy");
                await srcBlob.DeleteAsync();
                log.LogInformation("MoveBlob: Source file deleted");
            }
        }
    }
}

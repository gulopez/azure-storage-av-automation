using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Azure.Storage;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using System.Web;

namespace ScanUploadedBlobFunction
{
    public class ScannerProxy
    {
        private string hostIp { get; set; }
        private HttpClient client;
        private ILogger log { get; }
        private const string TARGET_CONTAINER_NAME = "targetContainerName";
        private const string DEFENDER_STORAGE = "windefenderstorage";
        private const string STORAGE_ENDPOINT = "storageendpointsuffix";
        private const string SAS_DURATION = "sasdurationhours";

        public ScannerProxy(ILogger log, string hostIp)
        {
            var handler = new HttpClientHandler();
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ServerCertificateCustomValidationCallback =
                (httpRequestMessage, cert, cetChain, policyErrors) =>
                {
                    return true;
                };
            this.hostIp = hostIp;
            this.log = log;
            client = new HttpClient(handler);
        }

        public ScanResults Scan(Stream blob, string blobName)
        {
            string srcContainerName = Environment.GetEnvironmentVariable(TARGET_CONTAINER_NAME);
            string connectionString = Environment.GetEnvironmentVariable(DEFENDER_STORAGE);
            string storageendpointsuffix = Environment.GetEnvironmentVariable(STORAGE_ENDPOINT);
            string sasDuration = Environment.GetEnvironmentVariable(SAS_DURATION);
            int SASTokenDurationHours = Convert.ToInt32(sasDuration);

            string[] accountdetails = connectionString.Split(';');
            string[] accountnameinfo = accountdetails[1].Split('=');
            string accountname = accountnameinfo[1];
            string[] accountkeyinfo =  accountdetails[2].Split('=');
            string accountkey = accountkeyinfo[1];

            log.LogInformation($"accountname: {accountname}");
           // log.LogInformation($"accountkey {accountkey}");

            //Generate SAS Token
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(srcContainerName);
            SharedAccessBlobPermissions permission = SharedAccessBlobPermissions.Read;
            TimeSpan clockSkew = TimeSpan.FromMilliseconds(1);
            TimeSpan accessDuration = TimeSpan.FromHours(SASTokenDurationHours);

            var blobSAS = new SharedAccessBlobPolicy
            {
                SharedAccessStartTime = DateTime.UtcNow.Subtract(clockSkew),
                SharedAccessExpiryTime = DateTime.UtcNow.Add(accessDuration) + clockSkew,
                Permissions = permission
            };

            CloudBlockBlob blob1 = container.GetBlockBlobReference(blobName);
            string sasToken = blob1.GetSharedAccessSignature(blobSAS);
                    

           // log.LogInformation($"sasToken {sasToken}");
            //Encode
            var tokenBytes = Encoding.UTF8.GetBytes(sasToken);
            string encodedsas = Convert.ToBase64String(tokenBytes);
            //log.LogInformation($"sasToken encoded: {encodedsas}");
            
            //Decode
            var base64EncodedSASBytes = Convert.FromBase64String(encodedsas);
            var decodedSas = Encoding.UTF8.GetString(base64EncodedSASBytes);
            //log.LogInformation($"sasToken decoded {decodedSas}");

            //Encode
            log.LogInformation($"storageendpointsuffix: {storageendpointsuffix}");
            var endpointBytes = Encoding.UTF8.GetBytes(storageendpointsuffix);
            string encodedendpoint = Convert.ToBase64String(endpointBytes);
          //  log.LogInformation($"endpoint sufix encoded {encodedendpoint}");

            //Decode
            var base64EncodedEndpointBytes = Convert.FromBase64String(encodedendpoint);
            var decodedEndpoint = Encoding.UTF8.GetString(base64EncodedEndpointBytes);
            log.LogInformation($"sasToken decoded {decodedEndpoint}");

            string blobnameencoded = HttpUtility.UrlEncode(blobName);

            string url = String.Format("https://" + hostIp + "/scan?blobname={0}&ContainerName={1}&sastoken={2}&accountname={3}&storagesuffix={4}", blobnameencoded, srcContainerName, encodedsas, accountname, encodedendpoint);
            log.LogInformation($"url {url}");


            //  var form = CreateMultiPartForm(blob, blobName);
            log.LogInformation($"Posting request to {url}");
            var response = client.GetAsync(url).Result;

            log.LogInformation($"Returning from scan");

            string stringContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                log.LogError($"Request Failed, {response.StatusCode}:{stringContent}");
                return null;
            }
            log.LogInformation($"Request Success Status Code:{response.StatusCode}");
            var responseDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(stringContent);
            var scanResults = new ScanResults(
                    fileName: blobName,
                    isThreat: Convert.ToBoolean(responseDictionary["isThreat"]),
                    threatType: responseDictionary["ThreatType"]
                );

            return scanResults;
        }

        private static MultipartFormDataContent CreateMultiPartForm(Stream blob, string blobName)
        {
            string boundry = GenerateRandomBoundry();
            MultipartFormDataContent form = new MultipartFormDataContent(boundry);
            var streamContent = new StreamContent(blob);
            var blobContent = new ByteArrayContent(streamContent.ReadAsByteArrayAsync().Result);
            blobContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
            form.Add(blobContent, "malware", blobName);
            return form;
        }

        private static string GenerateRandomBoundry()
        {
            const int maxBoundryLength = 69;
            const string src = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var stringBuilder = new StringBuilder();
            Random random = new Random();
            int length = random.Next(1, maxBoundryLength - 2);
            int numOfHyphens = (maxBoundryLength) - length;

            for (var i = 0; i < length; i++)
            {
                var c = src[random.Next(0, src.Length)];
                stringBuilder.Append(c);
            }
            string randomString = stringBuilder.ToString();
            string boundry = randomString.PadLeft(numOfHyphens, '-');
            return boundry;
        }
    }
}

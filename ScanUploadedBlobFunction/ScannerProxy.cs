using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace ScanUploadedBlobFunction
{
    public class ScannerProxy
    {
        private string hostIp { get; set; }
        private HttpClient client;
        private ILogger log { get; }
        private const string TARGET_CONTAINER_NAME = "targetContainerName";
        private const string DEFENDER_STORAGE = "windefenderstorage";
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
           // var srcContainer = new BlobContainerClient(connectionString, srcContainerName);

            string url = String.Format("https://" + hostIp + "/scan?blobname={0}&ContainerName={1}&connectionString={2}", blobName, srcContainerName, connectionString);
            var form = CreateMultiPartForm(blob, blobName);
            log.LogInformation($"Posting request to {url}");
            var response = client.PostAsync(url, form).Result;
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

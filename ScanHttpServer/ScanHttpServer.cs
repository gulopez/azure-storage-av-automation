using HttpMultipartParser;
using Newtonsoft.Json;
using Serilog;
using Serilog.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Web;

namespace ScanHttpServer
{
    public class ScanHttpServer
    {
        
        private enum requestType { SCAN }

        public static async Task HandleRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            Log.Information("Got new request {requestUrl}", request.Url);
            Log.Information("Raw URL: {requestRawUrl}", request.RawUrl);
            Log.Information("request.ContentType: {requestContentType}", request.ContentType);

            ScanRequest(request, response);

            Log.Information("Done Handling Request {requestUrl}", request.Url);
        }

        public static void ScanRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            //if (!request.ContentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            //{
            //    Log.Error("Wrong request Content-type for scanning, {requestContentType}", request.ContentType);
            //    return;
            //};

            try
            {
                string blobname = request.QueryString["blobname"];
                blobname = HttpUtility.UrlDecode(blobname);
               //Log.Information("blobname: {fileName}", blobname);

                string ContainerName = request.QueryString["ContainerName"];
               //Log.Information("ContainerName: {ContainerName}", ContainerName);

                string accountname = request.QueryString["accountname"];
                //Log.Information("accountname: {accountname}", accountname);

                string storagesuffixencoded = request.QueryString["storagesuffix"];
               // Log.Information("storagesuffix: {storagesuffix}", storagesuffixencoded);

                //Decode
                var base64EncodedEndpointBytes = Convert.FromBase64String(storagesuffixencoded);
                var decodedEndpoint = Encoding.UTF8.GetString(base64EncodedEndpointBytes);
               // Log.Information($"sasToken decoded {decodedEndpoint}");

                string sastokenencoded = request.QueryString["sastoken"];
               // Log.Information("sastoken: {sastoken}", sastokenencoded);

                //Decode
                var base64EncodedSASBytes = Convert.FromBase64String(sastokenencoded);
                var decodedSas = Encoding.UTF8.GetString(base64EncodedSASBytes);
               // Log.Information($"sasToken decoded {decodedSas}");

                string sourceurl = string.Format("https://{0}.{1}/{2}/{3}{4}", accountname, decodedEndpoint, ContainerName, blobname, decodedSas);
               // Log.Information("Source URL: {sourceurl}", sourceurl);

                string tempFileName = Path.GetTempFileName();
                Log.Information("Generate a Temp File : {tempFileName}", tempFileName);

                string azcommand = string.Format("azcopy copy {0} {1} --preserve-smb-permissions=false --preserve-smb-info=false", sourceurl, tempFileName);
                Log.Information("Run Az Copy : {azcommand}", azcommand);

                var AzCopyProcess = new Process();
                AzCopyProcess.StartInfo.UseShellExecute = false;
                AzCopyProcess.StartInfo.RedirectStandardOutput = true;
                AzCopyProcess.StartInfo.FileName = "azcopy.exe";
                //pass storage account name, container and the key

                string arguments = string.Format(@"copy ""{0}""  ""{1}""", sourceurl, tempFileName);

                AzCopyProcess.StartInfo.Arguments = arguments;

                Log.Information("Starting AzCopy");
                AzCopyProcess.Start();
           
                AzCopyProcess.WaitForExit();
                Log.Information("AzCopy completed");

                //Check File Sizes to make sure was copy correctly
                //FileInfo fi = new FileInfo(tempFileName);
                //long size = fi.Length;
                try 
                {
                    long length = new System.IO.FileInfo(tempFileName).Length;
                    Log.Information(string.Format("File size for {0}:{1}", tempFileName,length));
                }
                catch
                {
                    Log.Information(string.Format("Exception reading file size for {0}", tempFileName));
                }
                

                var scanner = new WindowsDefenderScanner();
                Log.Information("Scanning file");
                var result = scanner.Scan(tempFileName);

                if (result.isError )
                {
                    Log.Error("Error during the scanning Error message:{errorMessage}", result.errorMessage);

                    var data = new
                    {
                        ErrorMessage = result.errorMessage,
                    };

                    SendResponse(response, HttpStatusCode.InternalServerError, data);
                    return;
                }

                var responseData = new
                {
                    FileName = blobname,
                    isThreat = result.isThreat,
                    ThreatType = result.threatType
                };
                Log.Information("Sending response");
                SendResponse(response, HttpStatusCode.OK, responseData);

                try
                {
                    Log.Information("Deleting Local File");
                    File.Delete(tempFileName);
                }
                catch (Exception e)
                {
                        Log.Error(e, "Exception caught when trying to delete temp file:{tempFileName}.", tempFileName);
                }

            }
            catch (Exception ex)
            {
                Log.Information("Exception: {Message}", ex.Message);
            }  
          
        }

        private static void SendResponse(
            HttpListenerResponse response,
            HttpStatusCode statusCode,
            object responseData)
        {
            response.StatusCode = (int)statusCode;
            string responseString = JsonConvert.SerializeObject(responseData);
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            var responseOutputStream = response.OutputStream;
            try
            {
                responseOutputStream.Write(buffer, 0, buffer.Length);
            }
            finally
            {
                Log.Information("Sending response, {statusCode}:{responseString}", statusCode, responseString);
                responseOutputStream.Close();
            }
        }

        public static void SetUpLogger(string logFileName)
        {
            string runDirPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string logFilePath = Path.Combine(runDirPath, "log", logFileName);
            Log.Logger = new LoggerConfiguration()
                .Enrich.WithExceptionDetails()
                .WriteTo.File(logFilePath)
                .WriteTo.Console()
                .MinimumLevel.Debug()
                .CreateLogger();
        }

        public static void Main(string[] args)
        {
            int port = 443;
            string[] prefix = {
                $"https://+:{port}/"
            };

            SetUpLogger("ScanHttpServer.log");
            var listener = new HttpListener();

            foreach (string s in prefix)
            {
                listener.Prefixes.Add(s);
            }

            listener.Start();
            Log.Information("Starting ScanHttpServer");

            while (true)
            {
                Log.Information("Waiting for requests...");
                var context = listener.GetContext();
                Task.Run(() => HandleRequestAsync(context));
            }
        }
    }
}

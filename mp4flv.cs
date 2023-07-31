using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage.Blob;
using Azure.Core;
using BlobProperties = Azure.Storage.Blobs.Models.BlobProperties;

namespace converter_service_w_dotnet
{
    public class Mp4flv
    {
        public static readonly string incomingContainerName = "incoming-mp4-n-flv";
        public static readonly string processedContainerName = "processed-videos";
        public static readonly string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        [FunctionName("Mp4flv")]
        public static async Task Run(
          [BlobTrigger("incoming-mp4-n-flv/{name}", Connection = "AzureWebJobsStorage")] Stream input,
          string name,
          ILogger log)
        {
            // Create the blob service client to access the blob
            BlobContainerClient container = new BlobContainerClient(connectionString, incomingContainerName);
            BlobClient blobClient = container.GetBlobClient(name);
            // Get blob properties
            BlobProperties blobProperties = await blobClient.GetPropertiesAsync();
            string fromFormat = blobProperties.Metadata["from"];
            string toFormat = blobProperties.Metadata["to"];
            string userId = blobProperties.Metadata["uuid"];
            //string fromFormat = ".mp4";
            //string toFormat = ".flv";
            //string userId = "jackass";
            string inputExt = Path.GetExtension(name).ToLower();
            string outputName = userId + "-" + Guid.NewGuid().ToString() + toFormat;
            string inputFile = Path.GetTempFileName() + inputExt;
            using (FileStream fs = new FileStream(inputFile, FileMode.Create, FileAccess.Write))
            {
                input.CopyTo(fs);
            }
            string outputFile = Path.GetTempFileName() + toFormat;
            var process = new Process();
            //process.StartInfo.FileName = "ffmpeg";
            process.StartInfo.FileName = "C:\\home\\ffmpeg\\ffmpeg.exe";
            if (fromFormat == ".mp4")
                process.StartInfo.Arguments = $"-i {inputFile} -c:v flv -c:a mp3 {outputFile}";
            if (fromFormat == ".flv")
                process.StartInfo.Arguments = $"-i {inputFile} -c:v libx264 -crf 23 -preset medium -c:a aac -b:a 128k {outputFile}";
            process.Start();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                log.LogError("FFmpeg conversion failed");
                return;
            }
            using (FileStream output = File.OpenRead(outputFile))
            {
                await UploadBlobAsync(output, outputName, log);
            }

            File.Delete(inputFile);
            File.Delete(outputFile);
        }
        private static async Task UploadBlobAsync(Stream input, string name, ILogger log)
        {
            try
            {
                BlobContainerClient container = new BlobContainerClient(connectionString, processedContainerName);
                BlobClient blob = container.GetBlobClient(name);
                log.LogInformation($"Uploading to {name}");
                await blob.UploadAsync(input);
                log.LogInformation("Upload succeeded");
            }
            catch (Exception ex)
            {
                log.LogError("Upload failed", ex);
                throw;
            }
        }
    }

}
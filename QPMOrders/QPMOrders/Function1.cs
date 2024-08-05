using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace ZipAzureFunction
{
    public static class Function1
    {
        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            if (string.IsNullOrEmpty(requestBody))
            {
                return new BadRequestObjectResult("Invalid input. The request body is null or empty.");
            }

            try
            {
                // Deserialize the JSON request body into an array of file objects
                var files = JsonConvert.DeserializeObject<List<FileRequest>>(requestBody);

                using (var memoryStream = new MemoryStream())
                {
                    using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                    {
                        int fileIndex = 1;
                        foreach (var file in files)
                        {
                            // Use the provided filename or generate one if not provided
                            string fileName = string.IsNullOrEmpty(file.FileName) ? $"file{fileIndex}.xml" : file.FileName;
                            fileIndex++;

                            var entry = archive.CreateEntry(fileName);

                            using (var entryStream = entry.Open())
                            {
                                // Convert base64 string to byte[]
                                byte[] fileBytes = Convert.FromBase64String(file.FileContent);

                                // Write the content of the file to the entry stream
                                await entryStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                            }
                        }
                    }

                    // Reset memory stream position
                    memoryStream.Position = 0;

                    // Upload to Blob Storage
                    string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

                    string containerName = "ordersready";
                    string blobName = "files.zip";

                    BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
                    BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                    BlobClient blobClient = containerClient.GetBlobClient(blobName);

                    await blobClient.UploadAsync(memoryStream, new BlobHttpHeaders { ContentType = "application/zip" });

                    log.LogInformation("Zip file successfully uploaded to Blob Storage.");

                    return new OkObjectResult("Zip file successfully uploaded to Blob Storage.");
                }
            }
            catch (Exception ex)
            {
                log.LogError($"An error occurred: {ex.Message}");
                return new StatusCodeResult(500); // Internal Server Error
            }
        }

        public class FileRequest
        {
            public string FileName { get; set; } // Optional file name
            public string FileContent { get; set; } // This is a base64 string
        }
    }
}

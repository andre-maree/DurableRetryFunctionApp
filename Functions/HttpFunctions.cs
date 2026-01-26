using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Azure;
using DurableRetryDemoFunctionApp.Models;

namespace DurableRetryDemoFunctionApp;

public class HttpFunctions
{
    [Function("RetryDemoFunction_HttpStart")]
    public async Task<HttpResponseData> HttpStart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger("RetryDemoFunction_HttpStart");

        // Read request content and persist to blob
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        requestBody = string.IsNullOrWhiteSpace(requestBody) ? "{\"to\":\"someone@gmail.com\",\"subject\":\"Test\",\"body\":\"Hello\"}" : requestBody;

        string blobName = $"{Guid.NewGuid():N}";

        try
        {
            string storageConn = Environment.GetEnvironmentVariable("AzureWebJobsStorage")!;

            BlobServiceClient blobService = new(storageConn);
            BlobContainerClient container = blobService.GetBlobContainerClient("retry-demo-data");
            BlobClient blobClient = container.GetBlobClient(blobName);

            using MemoryStream contentStream = new(System.Text.Encoding.UTF8.GetBytes(requestBody));

            try
            {
                await blobClient.UploadAsync(contentStream, overwrite: false);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Container likely does not exist; create and retry
                await container.CreateIfNotExistsAsync();

                contentStream.Position = 0;

                await blobClient.UploadAsync(contentStream, overwrite: false);
            }
            catch (RequestFailedException ex)
            {
                logger.LogError(ex, "Failed to create blob container.");

                throw;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save request body to blob storage.");
        }

        // Function input comes from the request content.
        // Build config from environment once and pass into orchestrator input deterministically
        RetryPolicyConfig cfg = new()
        {
            MaxNumberOfAttempts = GetEnvInt("Retry_MaxNumberOfAttempts", 2000),
            FirstRetrySeconds = GetEnvInt("Retry_FirstRetrySeconds", 5),
            BackoffCoefficient = GetEnvDouble("Retry_BackoffCoefficient", 1.1125),
            MaxRetrySeconds = GetEnvInt("Retry_MaxRetrySeconds", 100),
            TotalTimeoutDate = DateTime.UtcNow.AddHours(GetEnvInt("Retry_TotalTimeoutHours", 100))
        };

        OrchestrationInput input = new() { Config = cfg, RetryCount = 0 };

        // Start the orchestration and pass the config input. The instance ID is the blob name.
        await client.ScheduleNewOrchestrationInstanceAsync("RetryOrchestrator", input: input, options: new StartOrchestrationOptions { InstanceId = blobName });

        logger.LogInformation("Started orchestration with ID = '{instanceId}'.", blobName);

        // Returns an HTTP 202 response with an instance management payload.
        return await client.CreateCheckStatusResponseAsync(req, blobName);
    }

    private static int GetEnvInt(string key, int defaultValue)
    {
        string? v = Environment.GetEnvironmentVariable(key);

        return int.TryParse(v, out int parsed) ? parsed : defaultValue;
    }

    private static double GetEnvDouble(string key, double defaultValue)
    {
        string? v = Environment.GetEnvironmentVariable(key);

        return double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : defaultValue;
    }
}
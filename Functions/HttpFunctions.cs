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
    /// <summary>
    /// HTTP-triggered function that:
    /// 1) Reads the incoming request body (or uses a default payload),
    /// 2) Persists it to Blob Storage (blob name acts as orchestration instance ID),
    /// 3) Builds retry configuration from environment variables,
    /// 4) Starts a Durable Functions orchestration and returns a 202 with status endpoints.
    /// </summary>
    [Function("RetryDemoFunction_HttpStart")]
    public async Task<HttpResponseData> HttpStart(
        // Anonymous GET/POST entry point for demo purposes; secure appropriately in production.
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
        // Durable client used to schedule and manage orchestration instances.
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger("RetryDemoFunction_HttpStart");

        // Read request content; if empty, use a default JSON payload to simulate an email request.
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        requestBody = string.IsNullOrWhiteSpace(requestBody) ? "{\"to\":\"someone@gmail.com\",\"subject\":\"Test\",\"body\":\"Hello\"}" : requestBody;

        // Generate a unique blob name (also used as the orchestration instance ID).
        string blobName = $"{Guid.NewGuid():N}";

        // Persist the request payload to Blob Storage so the activity can read it later.
        try
        {
            string storageConn = Environment.GetEnvironmentVariable("AzureWebJobsStorage")!;

            BlobServiceClient blobService = new(storageConn);
            BlobContainerClient container = blobService.GetBlobContainerClient("retry-demo-data");
            BlobClient blobClient = container.GetBlobClient(blobName);

            using MemoryStream contentStream = new(System.Text.Encoding.UTF8.GetBytes(requestBody));

            try
            {
                // Try upload; if the container doesn't exist, catch 404 and create it.
                await blobClient.UploadAsync(contentStream, overwrite: false);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Container not found; create it and retry the upload.
                await container.CreateIfNotExistsAsync();

                // Reset stream position before retrying the upload.
                contentStream.Position = 0;

                await blobClient.UploadAsync(contentStream, overwrite: false);
            }
            catch (RequestFailedException ex)
            {
                // Log and rethrow unexpected storage errors.
                logger.LogError(ex, "Failed to create blob container.");

                throw;
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: log the error but continue to start the orchestration.
            logger.LogError(ex, "Failed to save request body to blob storage.");
        }

        // Build retry configuration from environment variables (with reasonable defaults).
        // This config is passed to the orchestrator to deterministically control retry behavior.
        RetryPolicyConfig cfg = new()
        {
            MaxNumberOfAttempts = GetEnvInt("Retry_MaxNumberOfAttempts", 2000),
            FirstRetrySeconds = GetEnvInt("Retry_FirstRetrySeconds", 5),
            BackoffCoefficient = GetEnvDouble("Retry_BackoffCoefficient", 1.1125),
            MaxRetrySeconds = GetEnvInt("Retry_MaxRetrySeconds", 100),
            TotalTimeoutDate = DateTime.UtcNow.AddHours(GetEnvInt("Retry_TotalTimeoutHours", 100))
        };

        // Initial orchestrator input: includes config and starting retry count.
        OrchestrationInput input = new() { Config = cfg, RetryCount = 0 };

        // Start the orchestration with a deterministic instance ID (blobName) so the activity can read the blob by ID.
        await client.ScheduleNewOrchestrationInstanceAsync(
            "RetryOrchestrator",
            input: input,
            options: new StartOrchestrationOptions { InstanceId = blobName });

        logger.LogInformation("Started orchestration with ID = '{instanceId}'.", blobName);

        // Return a 202 Accepted with links to check status, send event, and terminate via Durable Functions HTTP management APIs.
        return await client.CreateCheckStatusResponseAsync(req, blobName);
    }

    /// <summary>
    /// Reads an integer environment variable or returns a default if missing/invalid.
    /// </summary>
    private static int GetEnvInt(string key, int defaultValue)
    {
        string? v = Environment.GetEnvironmentVariable(key);

        return int.TryParse(v, out int parsed) ? parsed : defaultValue;
    }

    /// <summary>
    /// Reads a double environment variable (InvariantCulture) or returns a default if missing/invalid.
    /// </summary>
    private static double GetEnvDouble(string key, double defaultValue)
    {
        string? v = Environment.GetEnvironmentVariable(key);

        return double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : defaultValue;
    }
}
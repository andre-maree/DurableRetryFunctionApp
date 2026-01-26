using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using DurableRetryDemoFunctionApp.Models;

namespace DurableRetryDemoFunctionApp;

public class RetryActivity
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HttpClient _httpClient;

    public RetryActivity(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;

        _httpClient = _httpClientFactory.CreateClient("RetryDemoHttpClient");
    }
    
    /// <summary>
    /// Durable Activity function that executes an HTTP call, interprets the response,
    /// and returns retry guidance to the orchestrator.
    /// </summary>
    /// <param name="retrycount">Current attempt count provided by the orchestrator.</param>
    /// <param name="executionContext">Functions execution context (for logging and binding data).</param>
    /// <returns>
    /// A <see cref="RetryResult"/> indicating whether to retry and an optional delay in milliseconds.
    /// </returns>
    /// <remarks>
    /// - Success (2xx): returns <c>MustRetry = false</c>.
    /// - 429: attempts to honor Retry-After header; otherwise uses a default delay.
    /// - Non-retryable status codes: returns <c>MustRetry = false</c> with a negative delay encoding the status code.
    /// - Other failures: throws to let the durable retry policy apply.
    /// </remarks>
    [Function(nameof(HttpAction))] // Activity function name used by the orchestrator
    public async Task<RetryResult> HttpAction([ActivityTrigger] int retrycount, FunctionContext executionContext)
    {
        // Create a function-scoped logger for tracing within this activity execution.
        ILogger logger = executionContext.GetLogger("mylogger");

        // Read the request payload from Blob Storage using the orchestration instanceId as the blob name.
        // This simulates retrieving input data for the action being executed.
        string content = await ReadRequestBlobAsync(executionContext.BindingContext.BindingData["instanceId"].ToString());

        // Perform the HTTP call to a demo endpoint that can return various status codes.
        // Here we force 429 to exercise the retry logic and pass the current retry count.
        using HttpResponseMessage respo = await _httpClient.GetAsync($"ExecuteDemoAction?statuscode=429&retrycount={retrycount}");

        // If the response indicates success (2xx), no further retry is required.
        if (respo.IsSuccessStatusCode)
        {
            logger.LogWarning("Success statuscode returned.");

            return new RetryResult { MustRetry = false };
        }

        // Evaluate the status code to determine retry behavior.
        int statuscode = (int)respo.StatusCode;

        // Special handling for 429 (Too Many Requests): respect Retry-After if provided.
        if (statuscode == 429)
        {
            logger.LogError("429 statuscode returned. Retrycount: " + retrycount);

            // Attempt to read Retry-After header to derive an appropriate delay.
            System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter = respo.Headers.RetryAfter;

            if (retryAfter != null)
            {
                int delayMs = ParseRetryHeader(retryAfter);

                if (delayMs > 0)
                {
                    // Indicate that the orchestrator should retry after the computed delay.
                    return new RetryResult { MustRetry = true, DelayMilliseconds = delayMs };
                }
            }

            // Fallback default delay when Retry-After is not present.
            return new RetryResult { MustRetry = true, DelayMilliseconds = 100000 };
        }

        // If the status code is in the non-retryable set, surface failure without retry.
        if (nonRetryStatusCodes.Contains(statuscode))
        {
            // Use a negative DelayMilliseconds to encode the status code to the orchestrator, if desired.
            return new RetryResult { MustRetry = false, DelayMilliseconds = -statuscode };
        }

        // For other failures, throw an exception to trigger the durable retry policy configured in the orchestrator.
        throw new HttpRequestException("Failed with status code: " + respo.StatusCode);
    }

    private static async Task<string> ReadRequestBlobAsync(string blobname)
    {
        string storageConn = Environment.GetEnvironmentVariable("AzureWebJobsStorage")!;

        BlobServiceClient blobService = new(storageConn);
        BlobContainerClient container = blobService.GetBlobContainerClient("retry-demo-data");
        BlobClient blobClient = container.GetBlobClient(blobname);

        Azure.Response<Azure.Storage.Blobs.Models.BlobDownloadResult> download = await blobClient.DownloadContentAsync();

        string content = download.Value.Content.ToString();

        return content;
    }

    private static int ParseRetryHeader(System.Net.Http.Headers.RetryConditionHeaderValue retryAfter)
    {
        int delayMs;

        if (retryAfter.Delta.HasValue)
        {
            // Delta-seconds -> milliseconds
            delayMs = (int)Math.Round(retryAfter.Delta.Value.TotalMilliseconds);
        }
        else if (retryAfter.Date.HasValue)
        {
            // HTTP-date -> compute milliseconds until that date from now (UTC)
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset until = retryAfter.Date.Value;
            TimeSpan span = until - now;

            delayMs = (int)Math.Max(0, Math.Round(span.TotalMilliseconds));
        }
        else
        {
            delayMs = 10000; // fallback 10s
        }

        // Clamp between 2s and 120s
        if (delayMs < 2000) delayMs = 2000;

        return delayMs + 1000;
    }

    // HTTP status codes that should NOT be retried
    static readonly HashSet<int> nonRetryStatusCodes =
    [
        // 4xx (except 408, 429)
        400, 401, 403, 404, 405, 406, 407, 410, 411, 412, 413, 414, 415, 416, 417,
            421, 422, 423, 424, 426, 428, 431, 451,
            // Others
            501, 505
    ];
}
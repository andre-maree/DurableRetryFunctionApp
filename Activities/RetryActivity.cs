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
    
    [Function(nameof(ActionAsync))]
    public async Task<RetryResult> ActionAsync([ActivityTrigger] int retrycount, FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger("mylogger");

        string content = await ReadRequestBlobAsync(executionContext.BindingContext.BindingData["instanceId"].ToString());

        // This is where the actual SendGrid email sending would occur via the SendGrid SDK.
        // For this demo, we simulate the HTTP call to another function that returns various status codes.
        using HttpResponseMessage respo = await _httpClient.GetAsync($"ExecuteDemoAction?statuscode=429&retrycount={retrycount}");

        if (respo.IsSuccessStatusCode)
        {
            logger.LogCritical("Success statuscode returned.");

            return new RetryResult { MustRetry = false };
        }

        int statuscode = (int)respo.StatusCode;

        if (statuscode == 429)
        {
            logger.LogError("429 statuscode returned. Retrycount: " + retrycount);

            System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter = respo.Headers.RetryAfter;

            if (retryAfter != null)
            {
                int delayMs = ParseRetryHeader(retryAfter);

                if (delayMs > 0)
                {
                    return new RetryResult { MustRetry = true, DelayMilliseconds = delayMs };
                }
            }

            // Default delay for 429 if no Retry-After header
            return new RetryResult { MustRetry = true, DelayMilliseconds = 100000 };
        }

        if (nonRetryStatusCodes.Contains(statuscode))
        {
            return new RetryResult { MustRetry = false, DelayMilliseconds = -statuscode };
        }

        // This will use the durable function retry policy set in the orchestration
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
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using DurableRetryDemoFunctionApp.Models;

namespace DurableRetryDemoFunctionApp;

public class RetryOrchestration
{
    [Function(nameof(RetryOrchestrator))]
    public static async Task<string> RetryOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        OrchestrationInput input = context.GetInput<OrchestrationInput>();

        RetryPolicyConfig cfg = input.Config;

        TaskOptions opts = new()
        {
            Retry = TaskRetryOptions.FromRetryPolicy(
                    new RetryPolicy(
                        maxNumberOfAttempts: cfg.MaxNumberOfAttempts,
                        firstRetryInterval: TimeSpan.FromSeconds(cfg.FirstRetrySeconds),
                        backoffCoefficient: cfg.BackoffCoefficient,
                        maxRetryInterval: TimeSpan.FromSeconds(cfg.MaxRetrySeconds),
                        retryTimeout: cfg.TotalTimeoutDate - context.CurrentUtcDateTime
                    )
                )
        };

        int retrycount = input.RetryCount;

        // Call the activity function to send the email
        RetryResult result = await context.CallActivityAsync<RetryResult>("ActionAsync", input: retrycount, options: opts);

        // Determine whether to retry or complete
        if (!result.MustRetry)
        {
            // Circuit breaker conditions
            if (result.DelayMilliseconds < 0)
            {
                throw new HttpRequestException("Failed with status code: " + -result.DelayMilliseconds);
            }

            // Delete the request blob after successful email sending
            await context.CallActivityAsync("DeleteRequestBlobAsync", input, options: opts);

            return "completed";
        }
        else // Schedule a retry after recieving 429
        {
            // Circuit breaker condition
            if (retrycount >= cfg.MaxNumberOfAttempts)
            {
                throw new HttpRequestException("Failed with status code: 429. Max number of retry attempts reached.");
            }

            DateTime nextRetryDate = context.CurrentUtcDateTime.AddMilliseconds(result.DelayMilliseconds);

            // Circuit breaker condition
            if (nextRetryDate > cfg.TotalTimeoutDate)
            {
                throw new HttpRequestException("Failed with status code: 429. Total retry timeout reached.");
            }

            await context.CreateTimer(nextRetryDate, CancellationToken.None);

            int nextCount = retrycount + 1;

            input.RetryCount = nextCount;

            // Eternal orchestration: Continue with updated state
            context.ContinueAsNew(input);

            return $"retry-scheduled:{nextCount}|delay:{result.DelayMilliseconds}";
        }
    }
}
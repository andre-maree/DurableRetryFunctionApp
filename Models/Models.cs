namespace DurableRetryDemoFunctionApp.Models
{
    public sealed class RetryPolicyConfig
    {
        public int MaxNumberOfAttempts { get; set; }
        public int FirstRetrySeconds { get; set; }
        public double BackoffCoefficient { get; set; }
        public int MaxRetrySeconds { get; set; }
        public DateTime TotalTimeoutDate { get; set; }
    }

    public sealed class OrchestrationInput
    {
        public RetryPolicyConfig Config { get; set; } = new RetryPolicyConfig();
        public int RetryCount { get; set; }
    }

    public class RetryResult
    {
        public bool MustRetry { get; set; }
        public int DelayMilliseconds { get; set; }
    }
}

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;

namespace DurableRetryDemoFunctionApp.Functions;

public class CleanupFunction
{
    [Function("CleanupFunction")]
    public async Task Run([TimerTrigger("0 0 3 * * Sun")] TimerInfo myTimer, [DurableClient] DurableTaskClient client)
    {
        // Purge orchestration instances created before 7 days ago
        int completedDays = 7;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-completedDays);

        // Delete Completed instances older than 7 days
        var result = await client.PurgeInstancesAsync(
            DateTimeOffset.MinValue,
            cutoff,
            statuses: new List<OrchestrationRuntimeStatus> { OrchestrationRuntimeStatus.Completed }
        );

        int failedDays = 100;

        var cutoff2 = DateTimeOffset.UtcNow.AddDays(-failedDays);

        // Delete Terminated, Suspended, and Failed instances older than 100 days
        result = await client.PurgeInstancesAsync(
            DateTimeOffset.MinValue,
            cutoff2,
            statuses: new List<OrchestrationRuntimeStatus> { OrchestrationRuntimeStatus.Terminated, OrchestrationRuntimeStatus.Suspended,
            OrchestrationRuntimeStatus.Failed }
        );
    }
}
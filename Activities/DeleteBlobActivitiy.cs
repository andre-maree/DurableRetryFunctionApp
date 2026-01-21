using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;

namespace DurableRetryDemoFunctionApp;

public class DeleteBlobActivitiy
{
   
    [Function(nameof(DeleteRequestBlobAsync))]
    public async Task DeleteRequestBlobAsync([ActivityTrigger] FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger("blobdelete");

        string storageConn = Environment.GetEnvironmentVariable("AzureWebJobsStorage")!;

        BlobServiceClient blobService = new(storageConn);
        BlobContainerClient container = blobService.GetBlobContainerClient("retry-demo-data");
        BlobClient blobClient = container.GetBlobClient(executionContext.BindingContext.BindingData["instanceId"].ToString());

        var result = await blobClient.DeleteIfExistsAsync();

        logger.LogCritical("Delete blob {blobName} - Success: {deleted}", blobClient.Name, result.Value);
    }
}
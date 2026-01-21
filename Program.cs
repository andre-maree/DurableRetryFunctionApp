using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

FunctionsApplicationBuilder builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Register HttpClient with localhost as the base address
builder.Services.AddHttpClient("RetryDemoHttpClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:7110/");
});

builder.Build().Run();

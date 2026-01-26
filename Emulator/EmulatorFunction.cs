using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DurableRetryDemoFunctionApp;

public class EmulatorFunction
{
    private readonly ILogger<EmulatorFunction> _logger;

    public EmulatorFunction(ILogger<EmulatorFunction> logger)
    {
        _logger = logger;
    }

    [Function("ExecuteDemoAction")]
    public async Task<IActionResult> ExecuteDemoAction([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req, int statuscode, int retrycount)
    {
        await Task.Delay(700);

        if(Random.Shared.Next(1, 3) == 2)
        {
            req.HttpContext.Response.Headers.RetryAfter = $"{5 + retrycount}";

            return new ContentResult { StatusCode = StatusCodes.Status429TooManyRequests };
        }

        //if (retrycount < 3)
        //{
        //    // Indicate delay time before retrying (in seconds)
        //    req.HttpContext.Response.Headers.RetryAfter = $"{5 + retrycount}";

        //    return new ContentResult { StatusCode = StatusCodes.Status429TooManyRequests };
        //}

        //return new StatusCodeResult(301);

        return new OkResult();
    }
}
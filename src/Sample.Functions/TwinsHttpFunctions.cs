using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Sample.Application;

namespace Sample.Functions;

public sealed class TwinsHttpFunctions(ITwinRepository repository)
{
    [Function("GetTwin")]
    public async Task<IActionResult> GetTwinAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{deviceId}")] HttpRequest request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var twin = await repository.GetAsync(deviceId, cancellationToken);
        return twin is null ? new NotFoundResult() : new OkObjectResult(twin.Value.Twin);
    }

    /// <summary>Readiness probe used by the integration tests.</summary>
    [Function("Health")]
    public static IActionResult Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request)
        => new OkObjectResult(new { status = "Healthy" });
}

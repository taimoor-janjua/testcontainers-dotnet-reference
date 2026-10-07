using WireMock.Admin.Mappings;
using WireMock.Client;

namespace Sample.IntegrationTests.Core;

/// <summary>Thin helpers over the WireMock admin API for the most common stubbing patterns.</summary>
public static class WireMockStubs
{
    public static Task StubGetJsonAsync(this IWireMockAdminApi admin, string path, object body, int statusCode = 200) =>
        admin.PostMappingAsync(new MappingModel
        {
            Guid = Guid.NewGuid(),
            Request = new RequestModel { Path = path, Methods = ["GET"] },
            Response = new ResponseModel
            {
                StatusCode = statusCode,
                BodyAsJson = body,
                Headers = new Dictionary<string, object> { ["Content-Type"] = "application/json" },
            },
        });

    public static Task StubGetStatusAsync(this IWireMockAdminApi admin, string path, int statusCode) =>
        admin.PostMappingAsync(new MappingModel
        {
            Guid = Guid.NewGuid(),
            Request = new RequestModel { Path = path, Methods = ["GET"] },
            Response = new ResponseModel { StatusCode = statusCode },
        });

    /// <summary>
    /// Simulates a transient outage: the first <paramref name="failures"/> calls return 503, subsequent calls
    /// return <paramref name="body"/>. Implemented with a WireMock scenario (state machine) per path.
    /// </summary>
    public static async Task StubGetJsonAfterFailuresAsync(this IWireMockAdminApi admin, string path, object body, int failures)
    {
        var scenario = $"flaky-{path}";
        for (var i = 0; i < failures; i++)
        {
            await admin.PostMappingAsync(new MappingModel
            {
                Guid = Guid.NewGuid(),
                Scenario = scenario,
                WhenStateIs = i == 0 ? null : $"failed-{i}",
                SetStateTo = i == failures - 1 ? "recovered" : $"failed-{i + 1}",
                Request = new RequestModel { Path = path, Methods = ["GET"] },
                Response = new ResponseModel { StatusCode = 503 },
            });
        }

        await admin.PostMappingAsync(new MappingModel
        {
            Guid = Guid.NewGuid(),
            Scenario = scenario,
            WhenStateIs = "recovered",
            Request = new RequestModel { Path = path, Methods = ["GET"] },
            Response = new ResponseModel
            {
                StatusCode = 200,
                BodyAsJson = body,
                Headers = new Dictionary<string, object> { ["Content-Type"] = "application/json" },
            },
        });
    }

    /// <summary>Number of requests WireMock received for <paramref name="path"/>.</summary>
    public static async Task<int> CountRequestsAsync(this IWireMockAdminApi admin, string path)
    {
        var requests = await admin.GetRequestsAsync();
        return requests.Count(r => string.Equals(r.Request?.Path, path, StringComparison.OrdinalIgnoreCase));
    }
}

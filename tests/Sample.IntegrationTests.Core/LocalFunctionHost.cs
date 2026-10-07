using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sample.IntegrationTests.Core;

/// <summary>
/// Supports debugging the app under test in the IDE while the emulators run in Docker.
/// The fixture writes <c>local.settings.json</c> for the app (no copy/paste), then waits until the developer
/// has started the app and its health endpoint responds.
/// </summary>
public static class LocalFunctionHost
{
    public static readonly Uri DefaultBaseAddress = new("http://localhost:7071");

    public static string WriteLocalSettings(string projectDirectory, IReadOnlyDictionary<string, string> values)
    {
        var settings = new JsonObject
        {
            ["IsEncrypted"] = false,
            ["Values"] = new JsonObject(values.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value)))),
        };

        var path = Path.Combine(projectDirectory, "local.settings.json");
        File.WriteAllText(path, settings.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep keys/URLs readable
        }));
        return path;
    }

    public static async Task WaitUntilHealthyAsync(
        Uri healthUrl,
        TimeSpan timeout,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var stopwatch = Stopwatch.StartNew();
        var nextReminder = TimeSpan.Zero;

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                using var response = await http.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    log($"Local function host is healthy at {healthUrl}.");
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            if (stopwatch.Elapsed >= nextReminder)
            {
                log($"Waiting for the function host at {healthUrl}, start it now (F5 / 'func start'). " +
                    $"{(timeout - stopwatch.Elapsed).TotalSeconds:F0}s remaining.");
                nextReminder = stopwatch.Elapsed + TimeSpan.FromSeconds(15);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new TimeoutException($"The local function host did not become healthy at {healthUrl} within {timeout}.");
    }
}

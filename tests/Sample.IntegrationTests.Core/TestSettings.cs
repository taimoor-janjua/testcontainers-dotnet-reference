namespace Sample.IntegrationTests.Core;

/// <summary>Environment switches that control how test suites run. Documented in docs/03-running-locally.md.</summary>
public static class TestSettings
{
    /// <summary>
    /// <c>RUN_FUNCTION_LOCALLY=true</c>: do not start the app container. Instead write the app's local settings
    /// pointing at the emulators and wait for the developer to start the app (F5) so breakpoints work.
    /// </summary>
    public static bool RunFunctionLocally => IsTrue("RUN_FUNCTION_LOCALLY");

    /// <summary>
    /// <c>BUILD_FUNCTION_IMAGE</c>: <c>always</c> (default) rebuilds the app image before the run so tests never
    /// exercise stale code; <c>missing</c> builds only if absent; <c>never</c> expects CI to have built it.
    /// </summary>
    public static string BuildFunctionImage =>
        Environment.GetEnvironmentVariable("BUILD_FUNCTION_IMAGE")?.Trim().ToLowerInvariant() is { Length: > 0 } mode ? mode : "always";

    /// <summary>How long to wait for a locally started (F5) function host to become healthy.</summary>
    public static TimeSpan LocalFunctionStartTimeout =>
        int.TryParse(Environment.GetEnvironmentVariable("LOCAL_FUNCTION_TIMEOUT_SECONDS"), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMinutes(5);

    private static bool IsTrue(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase);
}

public static class RepoPaths
{
    /// <summary>Repository root, found by walking up from the test binaries to the folder containing the .slnx file.</summary>
    public static string Root { get; } = FindRoot();

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>Folder for test artifacts (container logs, etc.). Uploaded by CI.</summary>
    public static string ArtifactsDirectory(string suite) =>
        Combine("TestResults", "container-logs", $"{suite}-{DateTime.UtcNow:yyyyMMdd-HHmmss}");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root (no *.slnx found above the test binaries).");
    }
}

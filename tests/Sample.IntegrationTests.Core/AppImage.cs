using System.Diagnostics;
using System.Text;

namespace Sample.IntegrationTests.Core;

/// <summary>
/// Makes sure the application image under test exists and is current, by running the .NET SDK container
/// publish (<c>dotnet publish -t:PublishContainer</c>). This keeps <c>dotnet test</c> a single command locally.
/// </summary>
public static class AppImage
{
    public static async Task EnsureBuiltAsync(
        string imageName,
        string projectPath,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var mode = TestSettings.BuildFunctionImage;
        var exists = await RunAsync("docker", ["image", "inspect", imageName], cancellationToken) is { ExitCode: 0 };

        if (mode == "never" || (mode == "missing" && exists))
        {
            if (!exists)
            {
                throw new InvalidOperationException(
                    $"Image '{imageName}' not found and BUILD_FUNCTION_IMAGE=never. Run scripts/build-function-image.ps1 first.");
            }

            log($"Using existing image {imageName} (BUILD_FUNCTION_IMAGE={mode}).");
            return;
        }

        var (repository, tag) = Split(imageName);
        log($"Building image {imageName} from {projectPath} ...");
        var stopwatch = Stopwatch.StartNew();

        var result = await RunAsync("dotnet",
        [
            "publish", projectPath,
            "-c", "Release",
            "-r", "linux-x64",
            "-t:PublishContainer",
            "--nologo",
            "-v", "q",
            $"-p:ContainerRepository={repository}",
            $"-p:ContainerImageTag={tag}",
        ], cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Building image '{imageName}' failed (exit {result.ExitCode}):{Environment.NewLine}{result.Output}");
        }

        log($"Built {imageName} in {stopwatch.Elapsed.TotalSeconds:F0}s.");
    }

    private static (string Repository, string Tag) Split(string imageName)
    {
        var index = imageName.LastIndexOf(':');
        return index > 0 ? (imageName[..index], imageName[(index + 1)..]) : (imageName, "latest");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoPaths.Root,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        var output = new StringBuilder();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        output.Append(await stdout).Append(await stderr);
        return (process.ExitCode, output.ToString());
    }
}

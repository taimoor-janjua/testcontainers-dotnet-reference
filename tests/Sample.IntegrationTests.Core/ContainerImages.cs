using System.Runtime.InteropServices;

namespace Sample.IntegrationTests.Core;

/// <summary>
/// Single source of truth for container images. Every image is pinned so runs are reproducible.
/// Override any image with an environment variable, e.g. <c>TC_IMAGE_COSMOS=...:vnext-latest</c>.
/// </summary>
public static class ContainerImages
{
    public static string Azurite => Get("AZURITE", "mcr.microsoft.com/azure-storage/azurite:3.37.0");

    public static string EventHubs => Get("EVENTHUBS", "mcr.microsoft.com/azure-messaging/eventhubs-emulator:2.2.1");

    public static string ServiceBus => Get("SERVICEBUS", "mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1");

    /// <summary>
    /// SQL backing store for the Service Bus emulator. SQL Server 2022 has no arm64 image and crashes under
    /// qemu emulation, so arm64 hosts (Apple Silicon, Windows on ARM) use Azure SQL Edge instead.
    /// </summary>
    public static string Sql => Get("SQL", IsArm64Host
        ? "mcr.microsoft.com/azure-sql-edge:1.0.7"
        : "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04");

    /// <summary>Cosmos DB "vNext" Linux emulator: multi-arch, fast startup, HTTP by default.</summary>
    public static string Cosmos => Get("COSMOS", "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-EN20260907");

    public static string WireMock => Get("WIREMOCK", "sheyenrath/wiremock.net-alpine:2.19.0");

    public static bool IsArm64Host => RuntimeInformation.OSArchitecture == Architecture.Arm64;

    private static string Get(string name, string defaultImage)
        => Environment.GetEnvironmentVariable($"TC_IMAGE_{name}") is { Length: > 0 } overridden ? overridden : defaultImage;
}

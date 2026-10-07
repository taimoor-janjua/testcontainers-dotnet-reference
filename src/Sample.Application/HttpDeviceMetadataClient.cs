using System.Net;
using System.Net.Http.Json;
using Sample.Domain;

namespace Sample.Application;

public interface IDeviceMetadataClient
{
    Task<DeviceMetadata> GetAsync(string deviceId, CancellationToken cancellationToken);
}

/// <summary>Calls the external enrichment API. A 404 means "device not registered" and is not an error.</summary>
public sealed class HttpDeviceMetadataClient(HttpClient httpClient) : IDeviceMetadataClient
{
    public async Task<DeviceMetadata> GetAsync(string deviceId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"devices/{Uri.EscapeDataString(deviceId)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return DeviceMetadata.Unknown;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeviceMetadata>(cancellationToken)
            ?? DeviceMetadata.Unknown;
    }
}

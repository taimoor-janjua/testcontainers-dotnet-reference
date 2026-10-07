using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Sample.Domain;

namespace Sample.Application;

public interface ITwinRepository
{
    Task<(DeviceTwin Twin, string ETag)?> GetAsync(string deviceId, CancellationToken cancellationToken);

    /// <summary>Saves the twin. Throws <see cref="ConcurrencyConflictException"/> if <paramref name="etag"/> is stale.</summary>
    Task SaveAsync(DeviceTwin twin, string? etag, CancellationToken cancellationToken);
}

public sealed class ConcurrencyConflictException(string deviceId)
    : Exception($"Twin '{deviceId}' was modified concurrently.");

public sealed class CosmosTwinRepository(CosmosClient client, IOptions<SampleOptions> options) : ITwinRepository
{
    private readonly Container _container = client.GetContainer(options.Value.CosmosDbDatabase, options.Value.CosmosDbContainer);

    public async Task<(DeviceTwin Twin, string ETag)?> GetAsync(string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<DeviceTwin>(deviceId, new PartitionKey(deviceId), cancellationToken: cancellationToken);
            return (response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SaveAsync(DeviceTwin twin, string? etag, CancellationToken cancellationToken)
    {
        var partitionKey = new PartitionKey(twin.DeviceId);
        try
        {
            if (etag is null)
            {
                await _container.CreateItemAsync(twin, partitionKey, cancellationToken: cancellationToken);
            }
            else
            {
                await _container.ReplaceItemAsync(twin, twin.Id, partitionKey,
                    new ItemRequestOptions { IfMatchEtag = etag }, cancellationToken);
            }
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            throw new ConcurrencyConflictException(twin.DeviceId);
        }
    }
}

using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sample.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application services. The same registration is used by the Functions host and by the
    /// integration tests, so the tests exercise the real wiring (serializer, resilience pipeline, etc.).
    /// </summary>
    public static IServiceCollection AddSampleApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SampleOptions>()
            .Bind(configuration)
            .Validate(o => !string.IsNullOrWhiteSpace(o.CosmosDbConnection), "CosmosDbConnection is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ServiceBusConnection), "ServiceBusConnection is required.")
            .Validate(o => Uri.IsWellFormedUriString(o.EnrichmentApiBaseUrl, UriKind.Absolute), "EnrichmentApiBaseUrl must be an absolute URL.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SampleOptions>>().Value;
            var clientOptions = new CosmosClientOptions
            {
                UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ApplicationName = "sample-functions",
            };

            if (options.CosmosDbUseEmulator)
            {
                clientOptions.ConnectionMode = ConnectionMode.Gateway;
                clientOptions.LimitToEndpoint = true;
            }

            return new CosmosClient(options.CosmosDbConnection, clientOptions);
        });

        services.AddSingleton(sp => new ServiceBusClient(sp.GetRequiredService<IOptions<SampleOptions>>().Value.ServiceBusConnection));
        services.AddSingleton(sp => sp.GetRequiredService<ServiceBusClient>()
            .CreateSender(sp.GetRequiredService<IOptions<SampleOptions>>().Value.AlertQueueName));

        services.AddSingleton<ITwinRepository, CosmosTwinRepository>();
        services.AddSingleton<IAlertPublisher, ServiceBusAlertPublisher>();
        services.AddSingleton<DeviceEventProcessor>();

        services.AddHttpClient<IDeviceMetadataClient, HttpDeviceMetadataClient>((sp, client) =>
            {
                var baseUrl = sp.GetRequiredService<IOptions<SampleOptions>>().Value.EnrichmentApiBaseUrl;
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            })
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 3;
                o.Retry.Delay = TimeSpan.FromMilliseconds(200);
            });

        return services;
    }
}

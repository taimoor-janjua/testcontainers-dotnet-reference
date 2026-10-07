# Testcontainers reference for .NET

A small Azure Functions app and the tests around it. It shows how to run a real app container against
Azure emulators with [Testcontainers for .NET](https://dotnet.testcontainers.org/).

The app reads device events from Event Hubs, looks up device metadata over HTTP, stores a device twin in
Cosmos DB and sends an alert to Service Bus when the temperature goes above 80.

## Layout

```
src/
  Sample.Domain                   business rules, no dependencies
  Sample.Application              Cosmos, Service Bus and HTTP code
  Sample.Functions                the Function App (Event Hub trigger + HTTP endpoints)
tests/
  Sample.UnitTests                plain unit tests, no Docker
  Sample.IntegrationTests.Core    reusable container setup (emulators, network, app container, logs)
  Sample.IntegrationTests         tests against the app running in a container
```

## What the integration tests start

| Container      | Image                                                   | Used for              |
|----------------|---------------------------------------------------------|-----------------------|
| Azurite        | `mcr.microsoft.com/azure-storage/azurite`               | Functions host storage, Event Hubs checkpoints |
| Event Hubs     | `mcr.microsoft.com/azure-messaging/eventhubs-emulator`  | input events          |
| Service Bus    | `mcr.microsoft.com/azure-messaging/servicebus-emulator` | alert queue           |
| SQL Server     | `mcr.microsoft.com/mssql/server` (SQL Edge on ARM64)    | needed by the Service Bus emulator |
| Cosmos DB      | `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-*` | twin storage |
| WireMock       | `sheyenrath/wiremock.net-alpine`                        | fake metadata API     |
| Function App   | `sample-functions:test`, built from `src/Sample.Functions` | the app under test |

All containers join one private Docker network. The app reaches the emulators by alias (`eventhubs`,
`cosmos`, ...). The tests reach everything through mapped host ports.

Every container is started once per test run (xUnit assembly fixture) and removed at the end.

## Run

You need Docker and the .NET 10 SDK.

```
dotnet test --project tests/Sample.UnitTests
dotnet test --project tests/Sample.IntegrationTests
```

The first integration run pulls images and builds the app image, so it takes a few minutes.

## Settings

| Variable | Default | What it does |
|----------|---------|--------------|
| `BUILD_FUNCTION_IMAGE` | `always` | `always` rebuilds the app image, `missing` builds only if absent, `never` expects it to exist |
| `RUN_FUNCTION_LOCALLY` | `false` | `true` skips the app container. Start the app yourself to debug it, see below |
| `LOCAL_FUNCTION_TIMEOUT_SECONDS` | `300` | how long to wait for the local app |
| `TC_IMAGE_<NAME>` | | override an emulator image, for example `TC_IMAGE_COSMOS` |

You can also build the image yourself with `scripts/build-function-image.ps1` or `.sh`.

## Debug the app

1. Set `RUN_FUNCTION_LOCALLY=true` and run the integration tests.
2. The tests start the emulators and write `src/Sample.Functions/local.settings.json`.
3. Start the Function App from your IDE or with `func start`.
4. The tests wait for `http://localhost:7071/api/health` and then run against your app.

## Logs

Logs from every container, including the app, are saved to `TestResults/container-logs/integration-<time>/`.
Look there first when a test fails. CI uploads this folder.

## Notes

- ARM64 (Apple Silicon, Windows on ARM): SQL Server does not run under emulation, so SQL Edge is used.
  The app image is linux-x64, and the fixture sets `DOTNET_EnableWriteXorExecute=0` so it runs under emulation.
- If nuget.org is blocked on your network, point `nuget.config` to your internal feed.
- CI is in `.github/workflows/ci.yml`. It runs unit tests, builds the app image, then runs the integration tests.

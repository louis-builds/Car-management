using Microsoft.Azure.Devices;

namespace CarBattery.Functions;

// Shared RegistryManager connection, used by both BatteryFunctions (HTTP)
// and DeviceConnectionFunctions (Event Grid). Retry logic lives separately
// in RetryPolicy - see that file for why.
internal static class IotHubClient
{
    public static readonly RegistryManager RegistryManager = RegistryManager.CreateFromConnectionString(
        Environment.GetEnvironmentVariable("IOTHUB_SERVICE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("IOTHUB_SERVICE_CONNECTION_STRING is not set."));
}

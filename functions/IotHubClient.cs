using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common.Exceptions;

namespace CarBattery.Functions;

// Shared RegistryManager + throttle-retry helper, used by both
// BatteryFunctions (HTTP) and DeviceConnectionFunctions (Event Grid).
internal static class IotHubClient
{
    public static readonly RegistryManager RegistryManager = RegistryManager.CreateFromConnectionString(
        Environment.GetEnvironmentVariable("IOTHUB_SERVICE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("IOTHUB_SERVICE_CONNECTION_STRING is not set."));

    public static readonly string DeviceId =
        Environment.GetEnvironmentVariable("IOTHUB_DEVICE_ID")
        ?? throw new InvalidOperationException("IOTHUB_DEVICE_ID is not set.");

    // ponytail: fixed 3 attempts / exponential backoff, no Polly dependency
    // for a handful of call sites. Revisit with a proper policy library if
    // more endpoints need this or the backoff needs to be configurable.
    public static async Task<T> WithThrottleRetryAsync<T>(Func<Task<T>> operation)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (ThrottlingException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
        }
    }
}

using Microsoft.Azure.Devices.Common.Exceptions;

namespace CarBattery.Functions;

// Generic retry helper - no dependency on IoT Hub connection setup, so it's
// testable on its own (IotHubClient's static fields would otherwise force a
// real RegistryManager connection string to exist just to run a test).
public static class RetryPolicy
{
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

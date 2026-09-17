using System.Text.Json;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using static CarBattery.Functions.IotHubClient;
using static CarBattery.Functions.RetryPolicy;

namespace CarBattery.Functions;

public class DeviceConnectionFunctions
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Function("OnDeviceConnectionChanged")]
    public async Task Run([EventGridTrigger] EventGridEvent eventGridEvent, FunctionContext context)
    {
        var logger = context.GetLogger("OnDeviceConnectionChanged");
        logger.LogInformation(
            "Device connection event: {EventType} subject={Subject} data={Data}",
            eventGridEvent.EventType, eventGridEvent.Subject, eventGridEvent.Data);

        // The event carries which device it's about - multiple cars means
        // this can no longer assume it's always the same one.
        var eventData = JsonSerializer.Deserialize<ConnectionEventData>(eventGridEvent.Data.ToString(), JsonOptions);
        if (eventData?.DeviceId is not string deviceId)
        {
            return;
        }

        if (eventGridEvent.EventType == "Microsoft.Devices.DeviceDisconnected")
        {
            // Was it charging at the moment it dropped off? reported.isCharging
            // is the device's own last word on that before it went silent.
            Twin twin = await WithThrottleRetryAsync(() => RegistryManager.GetTwinAsync(deviceId));
            bool wasCharging = twin.Properties.Reported.Contains("isCharging")
                && (bool)twin.Properties.Reported["isCharging"];

            if (wasCharging)
            {
                var patch = new Twin();
                patch.Tags["alert"] = "unexpected_disconnect_while_charging";
                patch.Tags["alertAt"] = DateTime.UtcNow;
                await WithThrottleRetryAsync(() => RegistryManager.UpdateTwinAsync(deviceId, patch, "*"));
                logger.LogWarning("{DeviceId} disconnected while charging - alert tag set.", deviceId);
            }
        }
        else if (eventGridEvent.EventType == "Microsoft.Devices.DeviceConnected")
        {
            // Car's back online - clear any standing alert. Setting a Twin
            // property to null deletes it.
            var patch = new Twin();
            patch.Tags["alert"] = null;
            patch.Tags["alertAt"] = null;
            await WithThrottleRetryAsync(() => RegistryManager.UpdateTwinAsync(deviceId, patch, "*"));
        }
    }

    private record ConnectionEventData(string DeviceId);
}

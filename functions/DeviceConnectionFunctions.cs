using Azure.Messaging.EventGrid;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using static CarBattery.Functions.IotHubClient;
using static CarBattery.Functions.RetryPolicy;

namespace CarBattery.Functions;

public class DeviceConnectionFunctions
{
    [Function("OnDeviceConnectionChanged")]
    public async Task Run([EventGridTrigger] EventGridEvent eventGridEvent, FunctionContext context)
    {
        var logger = context.GetLogger("OnDeviceConnectionChanged");
        logger.LogInformation(
            "Device connection event: {EventType} subject={Subject} data={Data}",
            eventGridEvent.EventType, eventGridEvent.Subject, eventGridEvent.Data);

        if (eventGridEvent.EventType == "Microsoft.Devices.DeviceDisconnected")
        {
            // Was it charging at the moment it dropped off? reported.isCharging
            // is the device's own last word on that before it went silent.
            Twin twin = await WithThrottleRetryAsync(() => RegistryManager.GetTwinAsync(DeviceId));
            bool wasCharging = twin.Properties.Reported.Contains("isCharging")
                && (bool)twin.Properties.Reported["isCharging"];

            if (wasCharging)
            {
                var patch = new Twin();
                patch.Tags["alert"] = "unexpected_disconnect_while_charging";
                patch.Tags["alertAt"] = DateTime.UtcNow;
                await WithThrottleRetryAsync(() => RegistryManager.UpdateTwinAsync(DeviceId, patch, "*"));
                logger.LogWarning("Device disconnected while charging - alert tag set.");
            }
        }
        else if (eventGridEvent.EventType == "Microsoft.Devices.DeviceConnected")
        {
            // Car's back online - clear any standing alert. Setting a Twin
            // property to null deletes it.
            var patch = new Twin();
            patch.Tags["alert"] = null;
            patch.Tags["alertAt"] = null;
            await WithThrottleRetryAsync(() => RegistryManager.UpdateTwinAsync(DeviceId, patch, "*"));
        }
    }
}

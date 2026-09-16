using System.Net;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using static CarBattery.Functions.IotHubClient;
using static CarBattery.Functions.RetryPolicy;

namespace CarBattery.Functions;

public class BatteryFunctions
{
    [Function("GetBattery")]
    public async Task<HttpResponseData> GetBattery(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "battery")] HttpRequestData req)
    {
        Twin twin = await WithThrottleRetryAsync(() => RegistryManager.GetTwinAsync(DeviceId));
        var reported = twin.Properties.Reported;
        var desired = twin.Properties.Desired;
        var tags = twin.Tags;

        DateTime? lastUpdated = reported.Contains("lastUpdated") ? (DateTime)reported["lastUpdated"] : (DateTime?)null;
        DateTime? alertAt = tags.Contains("alertAt") ? (DateTime?)tags["alertAt"] : null;

        // The DeviceConnected Event Grid event that clears this tag can lag
        // well behind the device actually reconnecting. Don't wait on it:
        // once the device has reported anything newer than the alert, it's
        // obviously back, so stop showing the alert even if the tag on the
        // Twin hasn't been cleared yet. (The Event Grid path still clears
        // the tag itself eventually - this just stops the UI from lying in
        // the meantime.)
        bool alertStillCurrent = tags.Contains("alert")
            && (lastUpdated == null || alertAt == null || lastUpdated <= alertAt);

        var result = new
        {
            batteryLevel = reported.Contains("batteryLevel") ? (double)reported["batteryLevel"] : (double?)null,
            isCharging = reported.Contains("isCharging") ? (bool)reported["isCharging"] : (bool?)null,
            lastUpdated,
            schedule = desired.Contains("schedule") ? (string)desired["schedule"] : null,
            alert = alertStillCurrent ? (string)tags["alert"] : null,
            alertAt = alertStillCurrent ? alertAt : null
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    [Function("SetCharging")]
    public async Task<HttpResponseData> SetCharging(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "charging")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<ChargingRequest>();
        if (body is null)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        var patch = new Twin();
        patch.Properties.Desired["targetCharging"] = body.Charging;
        await WithThrottleRetryAsync(() => RegistryManager.UpdateTwinAsync(DeviceId, patch, "*"));

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("SetSchedule")]
    public async Task<HttpResponseData> SetSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<ScheduleRequest>();
        if (body is null || !TimeSpan.TryParse(body.Time, out _))
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        var patch = new Twin();
        patch.Properties.Desired["schedule"] = body.Time;
        await WithThrottleRetryAsync(() => RegistryManager.UpdateTwinAsync(DeviceId, patch, "*"));

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private record ChargingRequest(bool Charging);
    private record ScheduleRequest(string Time);
}

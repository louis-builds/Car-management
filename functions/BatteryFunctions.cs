using System.Net;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using static CarBattery.Functions.IotHubClient;
using static CarBattery.Functions.RetryPolicy;

namespace CarBattery.Functions;

public class BatteryFunctions
{
    // ponytail: hardcoded to the two cars we actually have registered.
    // If this grows past a handful, replace with a real device query
    // (RegistryManager.CreateQuery) or a stored list - not worth the code
    // for two.
    private static readonly string[] KnownDeviceIds = ["simulated-car-01", "simulated-car-02"];

    [Function("GetCars")]
    public async Task<HttpResponseData> GetCars(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cars")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(KnownDeviceIds);
        return response;
    }

    [Function("GetBattery")]
    public async Task<HttpResponseData> GetBattery(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "battery")] HttpRequestData req)
    {
        string? deviceId = GetDeviceId(req);
        if (deviceId is null)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        Twin twin = await WithThrottleRetryAsync(() => RegistryManager.GetTwinAsync(deviceId));
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
        string? deviceId = GetDeviceId(req);
        var body = await req.ReadFromJsonAsync<ChargingRequest>();
        if (deviceId is null || body is null)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        var patch = new Twin();
        patch.Properties.Desired["targetCharging"] = body.Charging;
        await WithThrottleRetryAsync(() => RegistryManager.UpdateTwinAsync(deviceId, patch, "*"));

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("SetSchedule")]
    public async Task<HttpResponseData> SetSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule")] HttpRequestData req)
    {
        string? deviceId = GetDeviceId(req);
        var body = await req.ReadFromJsonAsync<ScheduleRequest>();
        if (deviceId is null || body is null || !TimeSpan.TryParse(body.Time, out _))
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        var patch = new Twin();
        patch.Properties.Desired["schedule"] = body.Time;
        await WithThrottleRetryAsync(() => RegistryManager.UpdateTwinAsync(deviceId, patch, "*"));

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private static string? GetDeviceId(HttpRequestData req)
    {
        string? deviceId = System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("deviceId");
        return string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
    }

    private record ChargingRequest(bool Charging);
    private record ScheduleRequest(string Time);
}

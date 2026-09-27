using System.Net;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json.Linq;
using static CarBattery.Functions.IotHubClient;
using static CarBattery.Functions.RetryPolicy;

namespace CarBattery.Functions;

public class BatteryFunctions
{
    // ponytail: hardcoded to the two cars we actually have registered.
    // If this grows past a handful, replace with a real device query
    // (RegistryManager.CreateQuery) or a stored list - not worth the code
    // for two.
    private static readonly string[] KnownDeviceIds = ["real-car-01", "simulated-car-01", "simulated-car-02"];

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
            schedule = ReadSchedule(desired),
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
        if (deviceId is null || body is null)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }
        if (!IsValidSchedule(body, out string? error))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync(error ?? "Invalid schedule.");
            return bad;
        }

        var patch = new Twin();
        patch.Properties.Desired["schedule"] = new { start = body.Start, end = body.End, days = body.Days, date = body.Date };
        await WithThrottleRetryAsync(() => RegistryManager.UpdateTwinAsync(deviceId, patch, "*"));

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("DeleteSchedule")]
    public async Task<HttpResponseData> DeleteSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "schedule")] HttpRequestData req)
    {
        string? deviceId = GetDeviceId(req);
        if (deviceId is null)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        var patch = new Twin();
        patch.Properties.Desired["schedule"] = null; // assigning null to a Twin property deletes it
        await WithThrottleRetryAsync(() => RegistryManager.UpdateTwinAsync(deviceId, patch, "*"));

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // Normalizes desired.schedule into a consistent { start, end, days, date }
    // shape for the frontend, regardless of whether the Twin is still holding
    // the old bare "HH:mm" string (pre-dates the schedule redesign) or the
    // new object written by SetSchedule above. Reads the Twin's actual JSON
    // rather than TwinCollection's dynamic indexer - the latter wraps a
    // Newtonsoft JValue, not a real System.String, so `raw is string` on it
    // silently fails even when the value really is a string (see the
    // matching fix and comment in device-simulator/CarSimulator.cs, where
    // that exact bug crashed the device against a legacy schedule).
    private static object? ReadSchedule(TwinCollection desired)
    {
        if (!desired.Contains("schedule")) return null;

        JObject wholeDesired = JObject.Parse(desired.ToJson());
        JToken? scheduleToken = wholeDesired["schedule"];
        if (scheduleToken is null || scheduleToken.Type == JTokenType.Null) return null;

        if (scheduleToken.Type == JTokenType.String)
        {
            return new { start = scheduleToken.Value<string>(), end = (string?)null, days = (string?)null, date = (string?)null };
        }

        return new
        {
            start = scheduleToken["start"]?.Value<string>(),
            end = scheduleToken["end"]?.Value<string>(),
            days = scheduleToken["days"]?.Value<string>(),
            date = scheduleToken["date"]?.Value<string>()
        };
    }

    // At least one of start/end must be set and parseable as a time; days
    // and date are mutually exclusive; days must be digits 0-6; date must be
    // a valid date. This is the boundary that actually rejects bad input -
    // BatterySimulation.ParseSchedule (device side) is deliberately lenient
    // because by the time it runs, there's no request left to reject.
    private static bool IsValidSchedule(ScheduleRequest body, out string? error)
    {
        error = null;

        bool hasStart = !string.IsNullOrWhiteSpace(body.Start);
        bool hasEnd = !string.IsNullOrWhiteSpace(body.End);
        if (!hasStart && !hasEnd)
        {
            error = "At least one of start or end is required.";
            return false;
        }
        if (hasStart && !TimeSpan.TryParse(body.Start, out _))
        {
            error = "start is not a valid time.";
            return false;
        }
        if (hasEnd && !TimeSpan.TryParse(body.End, out _))
        {
            error = "end is not a valid time.";
            return false;
        }

        bool hasDays = !string.IsNullOrWhiteSpace(body.Days);
        bool hasDate = !string.IsNullOrWhiteSpace(body.Date);
        if (hasDays && hasDate)
        {
            error = "days and date cannot both be set.";
            return false;
        }
        if (hasDays && !body.Days!.All(c => c is >= '0' and <= '6'))
        {
            error = "days must only contain digits 0-6.";
            return false;
        }
        if (hasDate && !DateOnly.TryParse(body.Date, out _))
        {
            error = "date is not a valid date.";
            return false;
        }

        return true;
    }

    private static string? GetDeviceId(HttpRequestData req)
    {
        string? deviceId = System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("deviceId");
        return string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
    }

    private record ChargingRequest(bool Charging);
    private record ScheduleRequest(string? Start, string? End, string? Days, string? Date);
}

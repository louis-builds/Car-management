using System.Net;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace CarBattery.Functions;

public class BatteryFunctions
{
    private static readonly RegistryManager _registryManager = RegistryManager.CreateFromConnectionString(
        Environment.GetEnvironmentVariable("IOTHUB_SERVICE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("IOTHUB_SERVICE_CONNECTION_STRING is not set."));

    private static readonly string _deviceId =
        Environment.GetEnvironmentVariable("IOTHUB_DEVICE_ID")
        ?? throw new InvalidOperationException("IOTHUB_DEVICE_ID is not set.");

    [Function("GetBattery")]
    public async Task<HttpResponseData> GetBattery(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "battery")] HttpRequestData req)
    {
        Twin twin = await _registryManager.GetTwinAsync(_deviceId);
        var reported = twin.Properties.Reported;
        var desired = twin.Properties.Desired;

        var result = new
        {
            batteryLevel = reported.Contains("batteryLevel") ? (double)reported["batteryLevel"] : (double?)null,
            isCharging = reported.Contains("isCharging") ? (bool)reported["isCharging"] : (bool?)null,
            lastUpdated = reported.Contains("lastUpdated") ? (DateTime)reported["lastUpdated"] : (DateTime?)null,
            schedule = desired.Contains("schedule") ? (string)desired["schedule"] : null
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
        await _registryManager.UpdateTwinAsync(_deviceId, patch, "*");

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
        await _registryManager.UpdateTwinAsync(_deviceId, patch, "*");

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private record ChargingRequest(bool Charging);
    private record ScheduleRequest(string Time);
}

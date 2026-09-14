# Car Battery IoT Demo

Interview take-home: a webpage where a car owner can see battery level /
charging state, start or stop charging, and set a daily charging schedule —
backed by Azure IoT Hub, a simulated car, and Azure Functions.

## Architecture

```
┌──────────────┐   Device Twin    ┌───────────┐  RegistryManager SDK  ┌──────────────┐   fetch   ┌─────────┐
│ device-      │ ⇄ (MQTT) ⇄       │ IoT Hub   │ ⇄                    │ Azure Function│ ⇄ (HTTP) ⇄│ frontend│
│ simulator    │                  │ (F1 free) │                      │ (HTTP trigger)│           │(vanilla)│
│ (C# console) │                  └───────────┘                      └──────────────┘           └─────────┘
└──────────────┘
```

- `device-simulator/` — a .NET console app that plays the role of the car.
  Runs a small state machine (Idle/Charging), writes its state to the
  device's Twin `reported` properties, and reacts to `desired` properties
  the cloud sets (`targetCharging`, `schedule`).
- `functions/` — 3 HTTP-triggered Azure Functions that translate REST calls
  into Twin reads/writes, using the IoT Hub Service SDK (`RegistryManager`).
- `frontend/` — a single static HTML page that polls the Functions API and
  lets you start/stop charging and set a schedule.

Why Device Twin instead of telemetry messages or Direct Methods, and other
design decisions, are written up in `CLAUDE.md` at the repo root.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local) (`npm install -g azure-functions-core-tools@4`)
- An Azure IoT Hub (free F1 tier is enough) with one device registered
  (IoT Hub > Devices > Add Device)

## Setup

**1. `device-simulator/`** — create `device-simulator/.env`:
```
IOTHUB_DEVICE_CONNECTION_STRING=HostName=<hub>.azure-devices.net;DeviceId=<deviceId>;SharedAccessKey=<key>
```
This is the connection string of the device you registered (Azure Portal >
your IoT Hub > Devices > your device > Primary Connection String).

**2. `functions/`** — create `functions/local.settings.json`:
```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "IOTHUB_SERVICE_CONNECTION_STRING": "<IoT Hub 'service' shared access policy connection string>",
    "IOTHUB_DEVICE_ID": "<same deviceId as above>"
  },
  "Host": { "CORS": "*" }
}
```
The service connection string comes from Azure Portal > your IoT Hub >
Shared access policies > `service` > Primary connection string. This is a
different, more privileged credential than the device's — see `CLAUDE.md`
for why the split exists.

Both `.env` and `local.settings.json` are gitignored — nothing here should
end up committed.

## Run all three

```bash
# terminal 1
cd device-simulator && dotnet run

# terminal 2
cd functions && func start

# then open frontend/index.html directly in a browser
```

Once all three are running: the webpage polls `/api/battery` every 5s,
Start/Stop Charging writes `desired.targetCharging`, and the time picker
writes `desired.schedule`. Changes typically show up in the browser within
one polling interval + one simulator tick (both ~5s).

## Testing without the frontend

```bash
curl http://localhost:7071/api/battery

curl -X POST http://localhost:7071/api/charging \
  -H "Content-Type: application/json" -d "{\"charging\":true}"

curl -X POST http://localhost:7071/api/schedule \
  -H "Content-Type: application/json" -d "{\"time\":\"02:00\"}"
```

## Scope

This is an MVP: no auth, no multi-device support, minimal input validation,
no offline indicator. These are deliberate cuts, not oversights — see the
"Phase 2" list in `CLAUDE.md`.

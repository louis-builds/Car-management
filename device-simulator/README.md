# device-simulator

A C# console app simulating the car, running as an Azure IoT Hub device.

## What it does

- Simulates battery state every 5 seconds (charges up while charging, drains
  slowly while idle, auto-stops at 100%).
- Talks to the cloud only through the Device Twin — no telemetry messages,
  no Direct Methods:
  - Subscribes to desired properties (`targetCharging` / `schedule`) as the
    cloud's "setpoint" (same role as a PLC setpoint tag).
  - Writes reported properties (`batteryLevel` / `isCharging` /
    `lastUpdated`) on state change, or every 30s as a heartbeat — the
    device's "actual state" (same role as a sensor's process value).
- Supports a daily charging schedule: set `desired.schedule` to `"HH:mm"`,
  and the device switches to charging automatically once that time hits
  (at most once per day).
- The local state machine keeps ticking even while offline — it doesn't
  depend on the cloud being reachable; reported properties resync
  automatically once reconnected.

## Setup

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download).
2. In the Azure Portal, register a device under your IoT Hub (Device
   management > Devices > Add Device) and grab its **Primary connection
   string**.
3. Create a `.env` file in this directory (`device-simulator/`, already
   gitignored):

   ```
   IOTHUB_DEVICE_CONNECTION_STRING=HostName=<hub>.azure-devices.net;DeviceId=<deviceId>;SharedAccessKey=<key>
   ```

   The app loads `.env` automatically at startup (walking up from the
   current directory if needed), so you don't have to set the environment
   variable by hand each time. A real environment variable works too and
   takes priority if both are set.

## Run

```bash
dotnet restore
dotnet run
```

Expected output on a normal start:

```
Car battery simulator started. Press Ctrl+C to stop.
Initial state -> battery: 55.0%, state: Idle
[reported] battery=54.7% state=Idle
```

## Verifying in the Azure Portal

**IoT Hub > Devices > your device > Device Twin**: `properties.reported`
should show `batteryLevel`/`isCharging`/`lastUpdated` updating. Manually set
`properties.desired.targetCharging` to `true` and save — within a few
seconds this terminal should print `desired.targetCharging=True ->
state=Charging`.

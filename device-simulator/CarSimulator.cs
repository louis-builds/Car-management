using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNetEnv;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;

namespace CarBattery.DeviceSimulator
{
    /// <summary>
    /// Simulates a car's battery/charging system as an Azure IoT device.
    /// State model: desired properties are the cloud's setpoint
    /// (targetCharging, schedule), reported properties are what the device
    /// actually observed (batteryLevel, isCharging, lastUpdated) - same
    /// split a PLC setpoint vs. sensor process value would use.
    /// </summary>
    internal class Program
    {
        private enum State { Idle, Charging }

        // --- Simulated car state ---------------------------------------------------
        private static State _state = State.Idle;
        private static double _batteryLevel = 55.0; // start at 55%
        private static string? _schedule;            // "HH:mm", from desired.schedule
        private static DateOnly? _scheduleFiredOn;    // guards against re-firing all day
        private static readonly object _stateLock = new();

        // How fast the simulation moves / how chatty it is. Tune for a faster demo.
        private const double ChargeRatePerTick = 1.0;  // % gained per tick while charging
        private const double DrainRatePerTick = 0.3;   // % lost per tick while idle
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

        private static DeviceClient? _deviceClient;
        private static DateTime _lastReportedAt = DateTime.MinValue;
        private static State _lastReportedState = State.Idle;
        private static double _lastReportedBatteryLevel = double.NaN;

        private static async Task Main(string[] args)
        {
            LoadDotEnv();

            string? connectionString = Environment.GetEnvironmentVariable("IOTHUB_DEVICE_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.WriteLine("ERROR: IOTHUB_DEVICE_CONNECTION_STRING is not set.");
                Console.WriteLine("Add it to a .env file next to device-simulator.csproj, e.g.:");
                Console.WriteLine("  IOTHUB_DEVICE_CONNECTION_STRING=HostName=<hub>.azure-devices.net;DeviceId=<id>;SharedAccessKey=<key>");
                return;
            }

            _deviceClient = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Mqtt);

            // IoT Hub's Device Connection State events (DeviceConnected /
            // DeviceDisconnected, used by the Event Grid subscription) don't
            // start being tracked for .NET-SDK devices until the device has
            // sent at least one device-to-cloud message. We otherwise only
            // talk over the Twin, so send one empty message here purely to
            // satisfy that prerequisite - not the start of a telemetry
            // pipeline.
            await _deviceClient.SendEventAsync(new Message(Encoding.UTF8.GetBytes("{}")));

            // Pick up whatever desired properties are already sitting in the twin
            // (e.g. someone set a schedule before this device ever connected),
            // then keep listening for future changes.
            Twin twin = await _deviceClient.GetTwinAsync();
            ApplyDesiredProperties(twin.Properties.Desired);
            await _deviceClient.SetDesiredPropertyUpdateCallbackAsync(
                (desired, _) => { ApplyDesiredProperties(desired); return Task.CompletedTask; }, null);

            await ReportTwinStateAsync(force: true);

            Console.WriteLine("Car battery simulator started. Press Ctrl+C to stop.");
            Console.WriteLine($"Initial state -> battery: {_batteryLevel:F1}%, state: {_state}");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            await RunLoopAsync(cts.Token);

            await _deviceClient.CloseAsync();
        }

        // --- .env loading ------------------------------------------------------------

        private static void LoadDotEnv() => Env.TraversePath().Load();

        // --- Desired properties: cloud -> device setpoint ---------------------------

        private static void ApplyDesiredProperties(TwinCollection desired)
        {
            lock (_stateLock)
            {
                if (desired.Contains("targetCharging"))
                {
                    bool target = desired["targetCharging"];
                    _state = target ? State.Charging : State.Idle;
                    Console.WriteLine($">>> desired.targetCharging={target} -> state={_state}");
                }

                if (desired.Contains("schedule"))
                {
                    _schedule = (string)desired["schedule"];
                    _scheduleFiredOn = null; // new schedule value, allow it to fire today
                    Console.WriteLine($">>> desired.schedule={_schedule}");
                }
            }
        }

        // --- Main loop: sample -> apply physics -> check schedule -> report ---------

        private static async Task RunLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                UpdateBattery();
                CheckSchedule();
                await ReportTwinStateAsync(force: false);

                try
                {
                    await Task.Delay(TickInterval, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private static void UpdateBattery()
        {
            lock (_stateLock)
            {
                bool wasCharging = _state == State.Charging;
                var (nextLevel, stillCharging) = BatterySimulation.ApplyTick(
                    _batteryLevel, wasCharging, ChargeRatePerTick, DrainRatePerTick);

                _batteryLevel = nextLevel;
                if (wasCharging && !stillCharging)
                {
                    _state = State.Idle; // overcharge protection, like a real car
                    Console.WriteLine("Battery reached 100% - charging stopped automatically.");
                }
            }
        }

        private static void CheckSchedule()
        {
            lock (_stateLock)
            {
                if (_state == State.Charging) return; // already charging, nothing to check

                var now = DateTime.Now;
                if (BatterySimulation.ShouldFireSchedule(_schedule, now, _scheduleFiredOn, TickInterval))
                {
                    _state = State.Charging;
                    _scheduleFiredOn = DateOnly.FromDateTime(now);
                    Console.WriteLine($">>> Schedule {_schedule} fired - charging started.");
                }
            }
        }

        // --- Reported properties: device -> cloud process value ---------------------

        private static async Task ReportTwinStateAsync(bool force)
        {
            State state;
            double battery;
            lock (_stateLock)
            {
                state = _state;
                battery = _batteryLevel;
            }

            double roundedBattery = Math.Round(battery, 1);

            
            bool stateChanged = state != _lastReportedState;
            bool batteryChanged = roundedBattery != _lastReportedBatteryLevel;
            bool heartbeatDue = DateTime.UtcNow - _lastReportedAt >= HeartbeatInterval;
            if (!force && !stateChanged && !batteryChanged && !heartbeatDue) return;

            var reported = new TwinCollection
            {
                ["batteryLevel"] = roundedBattery,
                ["isCharging"] = state == State.Charging,
                ["lastUpdated"] = DateTime.UtcNow
            };

            await _deviceClient!.UpdateReportedPropertiesAsync(reported);
            _lastReportedState = state;
            _lastReportedBatteryLevel = roundedBattery;
            _lastReportedAt = DateTime.UtcNow;
            Console.WriteLine($"[reported] battery={battery:F1}% state={state}");
        }
    }
}

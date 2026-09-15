namespace CarBattery.DeviceSimulator;

// Pure logic pulled out of Program.cs so it's testable without spinning up
// the whole app (no Twin, no static state, no IoT Hub connection).
public static class BatterySimulation
{
    public static (double BatteryLevel, bool StillCharging) ApplyTick(
        double batteryLevel, bool isCharging, double chargeRatePerTick, double drainRatePerTick)
    {
        if (isCharging)
        {
            double next = Math.Min(101.0, batteryLevel + chargeRatePerTick);
            bool stillCharging = next < 100.0; // overcharge protection: auto-stop at 100%
            return (next, stillCharging);
        }

        double drained = Math.Max(0.0, batteryLevel - drainRatePerTick);
        return (drained, false);
    }

    public static bool ShouldFireSchedule(
        string? schedule, DateTime now, DateOnly? alreadyFiredOn, TimeSpan tickWindow)
    {
        if (schedule == null) return false;
        if (!TimeSpan.TryParse(schedule, out var scheduledTime)) return false;

        var today = DateOnly.FromDateTime(now);
        bool alreadyFiredToday = alreadyFiredOn == today;
        bool withinTickWindow = Math.Abs((now.TimeOfDay - scheduledTime).TotalSeconds) < tickWindow.TotalSeconds;

        return withinTickWindow && !alreadyFiredToday;
    }
}

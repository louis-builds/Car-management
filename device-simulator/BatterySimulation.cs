namespace CarBattery.DeviceSimulator;

// A charging schedule as the device understands it, after unwrapping
// whatever shape the Twin's desired.schedule happened to be in (see
// CarSimulator.ParseScheduleFromTwin for that unwrapping). Both Start and
// End are optional - either toggle in the frontend's dialog can be off -
// but at least one must be set for a schedule to do anything.
//
// Days and Date are mutually exclusive: Days is a weekly repeat, a string
// of digits 0-6 (0=Sunday, matching DateTime.DayOfWeek) e.g. "12345" for
// Mon-Fri. Date is a one-time schedule for a single calendar date. Neither
// set means "every day" (this is also what an old-format schedule, a bare
// "HH:mm" string with no end/days/date, becomes once parsed).
public sealed record ChargeSchedule(TimeSpan? Start, TimeSpan? End, string? Days, DateOnly? Date);

// Pure logic pulled out of Program.cs so it's testable without spinning up
// the whole app (no Twin, no static state, no IoT Hub connection).
public static class BatterySimulation
{
    public static (double BatteryLevel, bool StillCharging) ApplyTick(
        double batteryLevel, bool isCharging, double chargeRatePerTick, double drainRatePerTick)
    {
        if (isCharging)
        {
            double next = Math.Min(100.0, batteryLevel + chargeRatePerTick);
            bool stillCharging = next < 100.0; // overcharge protection: auto-stop at 100%
            return (next, stillCharging);
        }

        double drained = Math.Max(0.0, batteryLevel - drainRatePerTick);
        return (drained, false);
    }

    // Parses the four raw string fields (as they arrive over the wire / out
    // of the Twin) into a ChargeSchedule. An invalid/unparseable start or
    // end is treated as absent rather than a hard failure - this is pure
    // logic with no request context to reject with an error; the API
    // boundary (functions/BatteryFunctions.cs) is what actually validates
    // input before it gets this far.
    public static ChargeSchedule? ParseSchedule(string? start, string? end, string? days, string? date)
    {
        TimeSpan? startTs = ParseTime(start);
        TimeSpan? endTs = ParseTime(end);

        if (startTs is null && endTs is null) return null;

        DateOnly? dateOnly = date is not null && DateOnly.TryParse(date, out var d) ? d : null;
        string? normalizedDays = string.IsNullOrEmpty(days) ? null : days;

        return new ChargeSchedule(startTs, endTs, normalizedDays, dateOnly);
    }

    private static TimeSpan? ParseTime(string? value) =>
        value is not null && TimeSpan.TryParse(value, out var t) ? t : null;

    // Should charging START on this tick?
    public static bool ShouldStartCharging(
        ChargeSchedule? schedule, DateTime now, DateOnly? alreadyFiredOn, TimeSpan tickWindow)
    {
        if (schedule?.Start is not TimeSpan start) return false;

        var today = DateOnly.FromDateTime(now);
        if (alreadyFiredOn == today) return false;
        if (!IsActiveToday(schedule, now)) return false;

        return Math.Abs((now.TimeOfDay - start).TotalSeconds) < tickWindow.TotalSeconds;
    }

    // Should charging STOP on this tick?
    //
    // Deliberately NOT gated by IsActiveToday - once charging has started
    // (whether by this schedule, a schedule from a previous day, or a
    // manual Start), the end time applies regardless of which calendar day
    // it lands on. That's what makes an overnight schedule (e.g. start
    // 22:00, end 06:00) work without special-casing midnight: the stop
    // guard is keyed off the day it actually fires on, not the day the
    // charge started on.
    public static bool ShouldStopCharging(
        ChargeSchedule? schedule, DateTime now, DateOnly? alreadyFiredOn, TimeSpan tickWindow)
    {
        if (schedule?.End is not TimeSpan end) return false;

        var today = DateOnly.FromDateTime(now);
        if (alreadyFiredOn == today) return false;

        return Math.Abs((now.TimeOfDay - end).TotalSeconds) < tickWindow.TotalSeconds;
    }

    // Is `now` a day this schedule's START applies to? A one-time schedule
    // self-expires: once its Date is in the past, this returns false
    // forever, with no separate "clear it" step needed.
    private static bool IsActiveToday(ChargeSchedule schedule, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);

        if (schedule.Date is DateOnly date) return date == today;
        if (schedule.Days is null) return true; // no explicit days -> every day

        int dayOfWeek = (int)now.DayOfWeek; // 0=Sunday..6=Saturday
        return schedule.Days.Contains((char)('0' + dayOfWeek));
    }
}

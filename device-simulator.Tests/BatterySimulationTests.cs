using CarBattery.DeviceSimulator;
using Xunit;

namespace CarBattery.DeviceSimulator.Tests;

public class BatterySimulationTests
{
    [Fact]
    public void ApplyTick_WhileCharging_IncreasesLevel()
    {
        var (level, stillCharging) = BatterySimulation.ApplyTick(
            batteryLevel: 50, isCharging: true, chargeRatePerTick: 2.0, drainRatePerTick: 0.3);

        Assert.Equal(52.0, level);
        Assert.True(stillCharging);
    }

    [Fact]
    public void ApplyTick_ReachingFull_AutoStopsCharging()
    {
        var (level, stillCharging) = BatterySimulation.ApplyTick(
            batteryLevel: 99, isCharging: true, chargeRatePerTick: 2.0, drainRatePerTick: 0.3);

        Assert.Equal(100.0, level);
        Assert.False(stillCharging); // overcharge protection
    }

    [Fact]
    public void ApplyTick_WhileIdle_DrainsAndFloorsAtZero()
    {
        var (level, _) = BatterySimulation.ApplyTick(
            batteryLevel: 0.1, isCharging: false, chargeRatePerTick: 2.0, drainRatePerTick: 0.3);

        Assert.Equal(0.0, level);
    }

    // --- ParseSchedule ---------------------------------------------------

    [Fact]
    public void ParseSchedule_BothStartAndEndNull_ReturnsNull()
    {
        Assert.Null(BatterySimulation.ParseSchedule(start: null, end: null, days: null, date: null));
    }

    [Fact]
    public void ParseSchedule_LegacyStartOnlyString_ParsesAsDailyStart()
    {
        var schedule = BatterySimulation.ParseSchedule(start: "02:00", end: null, days: null, date: null);

        Assert.NotNull(schedule);
        Assert.Equal(TimeSpan.FromHours(2), schedule!.Start);
        Assert.Null(schedule.End);
        Assert.Null(schedule.Days);
        Assert.Null(schedule.Date);
    }

    [Fact]
    public void ParseSchedule_InvalidStart_TreatedAsAbsent()
    {
        var schedule = BatterySimulation.ParseSchedule(start: "not-a-time", end: "06:00", days: null, date: null);

        Assert.NotNull(schedule);
        Assert.Null(schedule!.Start);
        Assert.Equal(TimeSpan.FromHours(6), schedule.End);
    }

    // --- ShouldStartCharging ----------------------------------------------

    [Fact]
    public void ShouldStartCharging_AtScheduledTime_NotYetFiredToday_ReturnsTrue()
    {
        var now = new DateTime(2026, 1, 1, 2, 0, 0); // Thursday
        var schedule = new ChargeSchedule(TimeSpan.FromHours(2), null, null, null);

        bool result = BatterySimulation.ShouldStartCharging(schedule, now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.True(result);
    }

    [Fact]
    public void ShouldStartCharging_AlreadyFiredToday_ReturnsFalse()
    {
        var now = new DateTime(2026, 1, 1, 2, 0, 0);
        var schedule = new ChargeSchedule(TimeSpan.FromHours(2), null, null, null);

        bool result = BatterySimulation.ShouldStartCharging(
            schedule, now, alreadyFiredOn: DateOnly.FromDateTime(now), tickWindow: TimeSpan.FromSeconds(5));

        Assert.False(result);
    }

    [Fact]
    public void ShouldStartCharging_OutsideTickWindow_ReturnsFalse()
    {
        var now = new DateTime(2026, 1, 1, 2, 5, 0);
        var schedule = new ChargeSchedule(TimeSpan.FromHours(2), null, null, null);

        bool result = BatterySimulation.ShouldStartCharging(schedule, now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.False(result);
    }

    [Fact]
    public void ShouldStartCharging_NoScheduleSet_ReturnsFalse()
    {
        bool result = BatterySimulation.ShouldStartCharging(null, DateTime.Now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.False(result);
    }

    [Fact]
    public void ShouldStartCharging_WeekdaysSet_MatchingDayOfWeek_ReturnsTrue()
    {
        var now = new DateTime(2026, 1, 5, 2, 0, 0); // 2026-01-05 is a Monday
        var schedule = new ChargeSchedule(TimeSpan.FromHours(2), null, "12345", null); // Mon-Fri

        bool result = BatterySimulation.ShouldStartCharging(schedule, now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.True(result);
    }

    [Fact]
    public void ShouldStartCharging_WeekdaysSet_NonMatchingDayOfWeek_ReturnsFalse()
    {
        var now = new DateTime(2026, 1, 4, 2, 0, 0); // 2026-01-04 is a Sunday
        var schedule = new ChargeSchedule(TimeSpan.FromHours(2), null, "12345", null); // Mon-Fri

        bool result = BatterySimulation.ShouldStartCharging(schedule, now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.False(result);
    }

    [Fact]
    public void ShouldStartCharging_OneTimeDate_OnThatDate_ReturnsTrue()
    {
        var now = new DateTime(2026, 3, 15, 2, 0, 0);
        var schedule = new ChargeSchedule(TimeSpan.FromHours(2), null, null, new DateOnly(2026, 3, 15));

        bool result = BatterySimulation.ShouldStartCharging(schedule, now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.True(result);
    }

    [Fact]
    public void ShouldStartCharging_OneTimeDate_OnADifferentDate_ReturnsFalse()
    {
        var now = new DateTime(2026, 3, 16, 2, 0, 0);
        var schedule = new ChargeSchedule(TimeSpan.FromHours(2), null, null, new DateOnly(2026, 3, 15));

        bool result = BatterySimulation.ShouldStartCharging(schedule, now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.False(result);
    }

    // --- ShouldStopCharging -------------------------------------------------

    [Fact]
    public void ShouldStopCharging_AtEndTime_NotYetFiredToday_ReturnsTrue()
    {
        var now = new DateTime(2026, 1, 1, 6, 0, 0);
        var schedule = new ChargeSchedule(TimeSpan.FromHours(22), TimeSpan.FromHours(6), null, null);

        bool result = BatterySimulation.ShouldStopCharging(schedule, now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.True(result);
    }

    [Fact]
    public void ShouldStopCharging_NoEndSet_ReturnsFalse()
    {
        var now = new DateTime(2026, 1, 1, 6, 0, 0);
        var schedule = new ChargeSchedule(TimeSpan.FromHours(22), null, null, null);

        bool result = BatterySimulation.ShouldStopCharging(schedule, now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.False(result);
    }

    [Fact]
    public void ShouldStopCharging_OvernightEnd_FiresOnNextCalendarDayRegardlessOfWeekdayFilter()
    {
        // Started Friday 22:00, Days = weekdays only. The stop must still
        // fire on Saturday morning even though Saturday isn't in Days -
        // ShouldStopCharging isn't gated by IsActiveToday.
        var now = new DateTime(2026, 1, 10, 6, 0, 0); // Saturday
        var schedule = new ChargeSchedule(TimeSpan.FromHours(22), TimeSpan.FromHours(6), "12345", null);

        bool result = BatterySimulation.ShouldStopCharging(schedule, now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.True(result);
    }
}

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

    [Fact]
    public void ShouldFireSchedule_AtScheduledTime_NotYetFiredToday_ReturnsTrue()
    {
        var now = new DateTime(2026, 1, 1, 2, 0, 0);
        bool result = BatterySimulation.ShouldFireSchedule(
            schedule: "02:00", now: now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.True(result);
    }

    [Fact]
    public void ShouldFireSchedule_AlreadyFiredToday_ReturnsFalse()
    {
        var now = new DateTime(2026, 1, 1, 2, 0, 0);
        bool result = BatterySimulation.ShouldFireSchedule(
            schedule: "02:00", now: now, alreadyFiredOn: DateOnly.FromDateTime(now), tickWindow: TimeSpan.FromSeconds(5));

        Assert.False(result);
    }

    [Fact]
    public void ShouldFireSchedule_OutsideTickWindow_ReturnsFalse()
    {
        var now = new DateTime(2026, 1, 1, 2, 5, 0);
        bool result = BatterySimulation.ShouldFireSchedule(
            schedule: "02:00", now: now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.False(result);
    }

    [Fact]
    public void ShouldFireSchedule_NoScheduleSet_ReturnsFalse()
    {
        bool result = BatterySimulation.ShouldFireSchedule(
            schedule: null, now: DateTime.Now, alreadyFiredOn: null, tickWindow: TimeSpan.FromSeconds(5));

        Assert.False(result);
    }
}

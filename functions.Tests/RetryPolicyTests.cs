using CarBattery.Functions;
using Microsoft.Azure.Devices.Common.Exceptions;
using Xunit;

namespace CarBattery.Functions.Tests;

public class RetryPolicyTests
{
    [Fact]
    public async Task WithThrottleRetryAsync_SucceedsFirstTry_DoesNotRetry()
    {
        int calls = 0;

        int result = await RetryPolicy.WithThrottleRetryAsync(() =>
        {
            calls++;
            return Task.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task WithThrottleRetryAsync_ThrottledTwice_SucceedsOnThirdAttempt()
    {
        int calls = 0;

        int result = await RetryPolicy.WithThrottleRetryAsync(() =>
        {
            calls++;
            if (calls < 3)
            {
                throw new ThrottlingException("throttled");
            }
            return Task.FromResult(99);
        });

        Assert.Equal(99, result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task WithThrottleRetryAsync_ThrottledEveryTime_GivesUpAfterThreeAttempts()
    {
        int calls = 0;

        await Assert.ThrowsAsync<ThrottlingException>(() =>
            RetryPolicy.WithThrottleRetryAsync<int>(() =>
            {
                calls++;
                throw new ThrottlingException("throttled");
            }));

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task WithThrottleRetryAsync_NonThrottlingException_DoesNotRetry()
    {
        int calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetryPolicy.WithThrottleRetryAsync<int>(() =>
            {
                calls++;
                throw new InvalidOperationException("not a throttling error");
            }));

        Assert.Equal(1, calls);
    }
}

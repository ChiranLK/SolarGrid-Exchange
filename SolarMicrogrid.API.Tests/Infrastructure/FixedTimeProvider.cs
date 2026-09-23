/*
 * FixedTimeProvider.cs
 * -----------------------------------------------------------------------------
 * Purpose : Supplies an explicitly controlled UTC server clock for deterministic
 *           reservation boundary and audit tests.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Tests.Infrastructure;

internal sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    internal FixedTimeProvider(DateTimeOffset utcNow)
    {
        // Require a UTC offset so tests cannot accidentally use local wall-clock time.
        _utcNow = utcNow.ToUniversalTime();
    }

    public override DateTimeOffset GetUtcNow()
    {
        // Return the fixed instant until a test deliberately advances it.
        return _utcNow;
    }

    internal void SetUtcNow(DateTimeOffset utcNow)
    {
        // Move the clock explicitly without waiting for real time to pass.
        _utcNow = utcNow.ToUniversalTime();
    }
}

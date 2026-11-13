using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class ClimberAuthTests
{
    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan delta) => Now += delta;
    }

    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);

    private static FakeClock Clock() => new(DateTimeOffset.UnixEpoch);

    private static ClimberAuth Create(FakeClock clock, string pin = "1234") => new(pin, Idle, clock);

    [Fact]
    public void CorrectPin_UnlocksSession()
    {
        var auth = Create(Clock());

        Assert.False(auth.IsUnlocked("s1"));
        Assert.True(auth.TryUnlock("s1", "1234"));
        Assert.True(auth.IsUnlocked("s1"));
    }

    [Fact]
    public void WrongPin_DoesNotUnlock()
    {
        var auth = Create(Clock());

        Assert.False(auth.TryUnlock("s1", "0000"));
        Assert.False(auth.IsUnlocked("s1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptySession_CannotUnlock(string? session)
    {
        var auth = Create(Clock());

        Assert.False(auth.TryUnlock(session, "1234"));
        Assert.False(auth.IsUnlocked(session));
    }

    [Fact]
    public void Session_LocksAfterIdleTimeout()
    {
        var clock = Clock();
        var auth = Create(clock);
        auth.TryUnlock("s1", "1234");

        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.True(auth.IsUnlocked("s1"));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(auth.IsUnlocked("s1"));
    }

    [Fact]
    public void Activity_ExtendsTheSession()
    {
        var clock = Clock();
        var auth = Create(clock);
        auth.TryUnlock("s1", "1234");

        clock.Advance(TimeSpan.FromMinutes(25));
        auth.Touch("s1");
        clock.Advance(TimeSpan.FromMinutes(25));

        // 50 minutes since unlock, but only 25 since the last activity.
        Assert.True(auth.IsUnlocked("s1"));

        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.False(auth.IsUnlocked("s1"));
    }

    [Fact]
    public void TouchOnUnknownSession_DoesNotUnlock()
    {
        var auth = Create(Clock());

        auth.Touch("ghost");

        Assert.False(auth.IsUnlocked("ghost"));
    }

    [Fact]
    public void Lock_RemovesSession()
    {
        var auth = Create(Clock());
        auth.TryUnlock("s1", "1234");

        auth.Lock("s1");

        Assert.False(auth.IsUnlocked("s1"));
    }

    [Fact]
    public void Sessions_AreIndependent()
    {
        var auth = Create(Clock());
        auth.TryUnlock("a", "1234");

        Assert.True(auth.IsUnlocked("a"));
        Assert.False(auth.IsUnlocked("b"));
    }

    [Fact]
    public void IdleTimeout_IsExposed()
    {
        Assert.Equal(Idle, Create(Clock()).IdleTimeout);
    }
}

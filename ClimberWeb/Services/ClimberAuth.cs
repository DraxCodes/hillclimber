namespace ClimberWeb.Services;

/// <summary>
/// Tracks which browser sessions have entered the PIN, with a sliding idle timeout.
/// The state lives on the server and is keyed by an opaque client session id, so a page
/// refresh keeps the session unlocked as long as it has not been idle past the timeout.
/// </summary>
public interface IClimberAuth
{
    /// <summary>How long a session may go without activity before it locks again.</summary>
    TimeSpan IdleTimeout { get; }

    /// <summary>Whether <paramref name="sessionId"/> is currently unlocked.</summary>
    bool IsUnlocked(string? sessionId);

    /// <summary>Attempts to unlock <paramref name="sessionId"/> with <paramref name="pin"/>.</summary>
    bool TryUnlock(string? sessionId, string? pin);

    /// <summary>Records activity for an already-unlocked session (sliding expiration).</summary>
    void Touch(string? sessionId);

    /// <summary>Immediately locks <paramref name="sessionId"/>.</summary>
    void Lock(string? sessionId);
}

/// <summary>In-memory <see cref="IClimberAuth"/>. Suitable for a single-user tool.</summary>
public sealed class ClimberAuth : IClimberAuth
{
    private readonly string _pin;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, DateTimeOffset> _lastActivity = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ClimberAuth(string pin, TimeSpan idleTimeout, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(pin);

        _pin = pin;
        IdleTimeout = idleTimeout;
        _clock = clock ?? TimeProvider.System;
    }

    public TimeSpan IdleTimeout { get; }

    public bool IsUnlocked(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_lastActivity.TryGetValue(sessionId, out var last))
            {
                return false;
            }

            if (_clock.GetUtcNow() - last > IdleTimeout)
            {
                _lastActivity.Remove(sessionId);
                return false;
            }

            return true;
        }
    }

    public bool TryUnlock(string? sessionId, string? pin)
    {
        if (string.IsNullOrEmpty(sessionId) || !string.Equals(pin, _pin, StringComparison.Ordinal))
        {
            return false;
        }

        lock (_gate)
        {
            _lastActivity[sessionId] = _clock.GetUtcNow();
        }

        return true;
    }

    public void Touch(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        lock (_gate)
        {
            // Only refresh sessions that were actually unlocked; never create one.
            if (_lastActivity.ContainsKey(sessionId))
            {
                _lastActivity[sessionId] = _clock.GetUtcNow();
            }
        }
    }

    public void Lock(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        lock (_gate)
        {
            _lastActivity.Remove(sessionId);
        }
    }
}

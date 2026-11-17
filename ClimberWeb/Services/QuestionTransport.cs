using Microsoft.Extensions.Configuration;

namespace ClimberWeb.Services;

/// <summary>How a run gets its questions. The plain producer is the default.</summary>
public enum QuestionTransportMode
{
    /// <summary>No channel: every run is <c>opencode run --format json</c> only.</summary>
    Off,

    /// <summary>A per-run <c>opencode serve --stdio</c> child carries the question channel.</summary>
    Serve,
}

/// <summary>
/// Reads <c>Climber:Questions</c> (<c>off</c> or <c>serve</c>) and builds one channel factory
/// for the host. It is deliberately narrow: an unset, empty or unrecognised value is
/// <see cref="QuestionTransportMode.Off"/>, so a typo can never start a second process.
/// </summary>
public sealed class QuestionTransport : IQuestionChannelFactory
{
    /// <summary>The configuration key, under the <c>Climber</c> section.</summary>
    public const string ConfigKey = "Climber:Questions";

    private readonly QuestionTransportMode _mode;
    private readonly ServeSupervisor _supervisor;

    public QuestionTransport(QuestionTransportMode mode, ServeSupervisor supervisor)
    {
        _mode = mode;
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    }

    public QuestionTransportMode Mode => _mode;

    public bool Enabled => _mode == QuestionTransportMode.Serve;

    public IQuestionChannel? Create() => Enabled ? new QuestionChannel(_supervisor) : null;

    /// <summary>Parses the configured value; anything but <c>serve</c> is off.</summary>
    public static QuestionTransportMode Parse(string? value) =>
        string.Equals(value?.Trim(), "serve", StringComparison.OrdinalIgnoreCase)
            ? QuestionTransportMode.Serve
            : QuestionTransportMode.Off;

    /// <summary>The host's factory, from configuration and a default supervisor.</summary>
    public static QuestionTransport FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new QuestionTransport(
            Parse(configuration[ConfigKey]),
            new ServeSupervisor(new ProcessServeFactory()));
    }
}
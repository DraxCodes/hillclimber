using System.Net.Http.Headers;

namespace ClimberWeb.Services;

/// <summary>
/// HTTP client for OpenRouter's spend surfaces. Every call is defensive: a missing key,
/// a non-success status, a network failure, or an unexpected body becomes an explicit
/// status/note on the returned <see cref="SpendReport"/>, never an exception.
/// </summary>
public sealed class OpenRouterClient : IOpenRouterClient
{
    private readonly HttpClient _http;
    private readonly OpenRouterOptions _options;

    /// <summary>Creates the client from DI-provided options.</summary>
    public OpenRouterClient(HttpClient http, Microsoft.Extensions.Configuration.IConfiguration configuration)
        : this(http, OpenRouterOptions.FromConfiguration(configuration))
    {
    }

    /// <summary>Creates the client with explicit options (used by tests).</summary>
    public OpenRouterClient(HttpClient http, OpenRouterOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<SpendReport> GetSpendAsync(CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();

        if (!_options.HasKey)
        {
            return new SpendReport(
                new OpenRouterConnection(
                    Configured: false,
                    Connected: false,
                    KeyLabel: null,
                    IsFreeTier: false,
                    Usage: null,
                    Limit: null,
                    LimitRemaining: null,
                    Message: "No OpenRouter API key is configured."),
                Account: null,
                Activity: Array.Empty<OpenRouterActivityRow>(),
                notes);
        }

        var connection = await CheckKeyAsync(cancellationToken).ConfigureAwait(false);

        OpenRouterAccount? account = null;
        IReadOnlyList<OpenRouterActivityRow> activity = Array.Empty<OpenRouterActivityRow>();

        if (_options.HasManagementKey)
        {
            account = await GetCreditsAsync(notes, cancellationToken).ConfigureAwait(false);
            activity = await GetActivityAsync(notes, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            notes.Add("Add a management key (Climber:OpenRouterManagementApiKey) to see account spend and the 30-day activity.");
        }

        return new SpendReport(connection, account, activity, notes);
    }

    private async Task<OpenRouterConnection> CheckKeyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = Authorized("key", _options.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return NotConnected($"OpenRouter rejected the key (HTTP {(int)response.StatusCode}).");
            }

            if (OpenRouterParser.ParseKey(body) is not { } key)
            {
                return NotConnected("OpenRouter returned an unexpected response for the key.");
            }

            return new OpenRouterConnection(
                Configured: true,
                Connected: true,
                key.Label,
                key.IsFreeTier,
                key.Usage,
                key.Limit,
                key.LimitRemaining,
                Message: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return NotConnected($"Could not reach OpenRouter: {ex.Message}");
        }
    }

    private async Task<OpenRouterAccount?> GetCreditsAsync(List<string> notes, CancellationToken cancellationToken)
    {
        try
        {
            using var request = Authorized("credits", _options.ManagementApiKey);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                notes.Add($"Account spend is unavailable (HTTP {(int)response.StatusCode}); the management key may be invalid.");
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var account = OpenRouterParser.ParseCredits(body);
            if (account is null)
            {
                notes.Add("Account spend is unavailable (unexpected response).");
            }

            return account;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            notes.Add($"Account spend is unavailable: {ex.Message}");
            return null;
        }
    }

    private async Task<IReadOnlyList<OpenRouterActivityRow>> GetActivityAsync(List<string> notes, CancellationToken cancellationToken)
    {
        try
        {
            using var request = Authorized("activity", _options.ManagementApiKey);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                notes.Add($"Activity is unavailable (HTTP {(int)response.StatusCode}); the management key may be invalid.");
                return Array.Empty<OpenRouterActivityRow>();
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return OpenRouterParser.ParseActivity(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            notes.Add($"Activity is unavailable: {ex.Message}");
            return Array.Empty<OpenRouterActivityRow>();
        }
    }

    private static OpenRouterConnection NotConnected(string message) =>
        new(Configured: true, Connected: false, KeyLabel: null, IsFreeTier: false, Usage: null, Limit: null, LimitRemaining: null, Message: message);

    private HttpRequestMessage Authorized(string path, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }
}

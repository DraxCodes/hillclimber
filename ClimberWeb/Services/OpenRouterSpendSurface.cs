using System.Net.Http.Headers;

namespace ClimberWeb.Services;

/// <summary>
/// HTTP client for an OpenRouter-style management surface. Any provider whose descriptor
/// uses <see cref="ProviderKinds.OpenRouter"/> gets one. Every call is defensive: a missing
/// key, a non-success status, a network failure, or an unexpected body becomes an explicit
/// status/note on the returned <see cref="SpendReport"/>, never an exception.
/// </summary>
public sealed class OpenRouterSpendSurface : IProviderSpend
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly string? _managementApiKey;
    private readonly string _displayName;

    /// <summary>Creates the surface for a registry descriptor, so any OpenRouter-style provider works.</summary>
    public OpenRouterSpendSurface(HttpClient http, ProviderDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(descriptor);

        _http = http;
        _baseUrl = descriptor.BaseUrl ?? ProviderRegistry.OpenRouterDefaultBaseUrl;
        _apiKey = descriptor.ApiKey;
        _managementApiKey = descriptor.ManagementApiKey;
        _displayName = descriptor.DisplayName;
    }

    private bool HasKey => !string.IsNullOrWhiteSpace(_apiKey);

    private bool HasManagementKey => !string.IsNullOrWhiteSpace(_managementApiKey);

    /// <inheritdoc />
    public async Task<SpendReport> GetSpendAsync(CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();

        if (!HasKey)
        {
            return new SpendReport(
                new ProviderConnection(
                    Configured: false,
                    Connected: false,
                    KeyLabel: null,
                    IsFreeTier: false,
                    Usage: null,
                    Limit: null,
                    LimitRemaining: null,
                    Message: $"No {_displayName} API key is configured."),
                Account: null,
                Activity: Array.Empty<ProviderActivityRow>(),
                notes);
        }

        var connection = await CheckKeyAsync(cancellationToken).ConfigureAwait(false);

        ProviderAccount? account = null;
        IReadOnlyList<ProviderActivityRow> activity = Array.Empty<ProviderActivityRow>();

        if (HasManagementKey)
        {
            account = await GetCreditsAsync(notes, cancellationToken).ConfigureAwait(false);
            activity = await GetActivityAsync(notes, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            notes.Add($"Add a management key for {_displayName} to see account spend and the 30-day activity.");
        }

        return new SpendReport(connection, account, activity, notes);
    }

    private async Task<ProviderConnection> CheckKeyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = Authorized("key", _apiKey);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return NotConnected($"{_displayName} rejected the key (HTTP {(int)response.StatusCode}).");
            }

            if (OpenRouterParser.ParseKey(body) is not { } key)
            {
                return NotConnected($"{_displayName} returned an unexpected response for the key.");
            }

            return new ProviderConnection(
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
            return NotConnected($"Could not reach {_displayName}: {ex.Message}");
        }
    }

    private async Task<ProviderAccount?> GetCreditsAsync(List<string> notes, CancellationToken cancellationToken)
    {
        try
        {
            using var request = Authorized("credits", _managementApiKey);
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

    private async Task<IReadOnlyList<ProviderActivityRow>> GetActivityAsync(List<string> notes, CancellationToken cancellationToken)
    {
        try
        {
            using var request = Authorized("activity", _managementApiKey);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                notes.Add($"Activity is unavailable (HTTP {(int)response.StatusCode}); the management key may be invalid.");
                return Array.Empty<ProviderActivityRow>();
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
            return Array.Empty<ProviderActivityRow>();
        }
    }

    private static ProviderConnection NotConnected(string message) =>
        new(Configured: true, Connected: false, KeyLabel: null, IsFreeTier: false, Usage: null, Limit: null, LimitRemaining: null, Message: message);

    private HttpRequestMessage Authorized(string path, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }
}
using ClimberWeb.Components;
using ClimberWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// Optional, gitignored local overrides (e.g. OpenRouter keys). Added before environment
// variables are re-applied so env vars still win, and nothing here is ever committed.
builder.Configuration
    .AddJsonFile(Path.Combine(builder.Environment.ContentRootPath, "appsettings.local.json"), optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Command execution + runs. Both are singletons so a run keeps running (and stays
// observable) across page reloads and mobile reconnects: the general opencode run is the
// default method, and the hillclimb loop wraps the same opencode step.
builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();

// A development-only transcript fixture, so a UI pass starts from the same recorded run
// every time. The environment gate is checked here and again inside the fixture itself; a
// Production host never registers it, even when CLIMBER_SEED is set. The hillclimb loop is
// real work rather than a transcript, so the fixture host replaces it with a refusing guard
// too: a playback host never drives opencode, whichever method is chosen.
var playback = PlaybackRunService.IsEnabled(builder.Environment.IsDevelopment());

// Questions ride a per-run `opencode serve --stdio` child. Shipped `appsettings.json` sets
// Climber:Questions to `serve`; an unset, empty or unrecognised value is `off`, so clearing the
// key is a safe revert and a typo can never start a second process.
builder.Services.AddSingleton<IQuestionChannelFactory>(sp =>
    QuestionTransport.FromConfiguration(sp.GetRequiredService<IConfiguration>()));

if (playback)
{
    builder.Services.AddSingleton<IRunService>(
        PlaybackRunService.FromEnvironment(builder.Environment.IsDevelopment(), builder.Environment.ContentRootPath));
    builder.Services.AddSingleton<IHillClimberService, PlaybackHillClimberService>();
}
else
{
    builder.Services.AddSingleton<IRunService, RunService>();
    builder.Services.AddSingleton<IHillClimberService, HillClimberService>();
}
builder.Services.AddSingleton<IFileBrowser, FileBrowser>();

// Sessions. Filesystem-backed in a real host; in memory under the transcript fixture, whose
// runs are a display rather than work, so a playback host never writes a session file. The
// coordinator is the single owner of the active session and its transcript, and subscribes
// to the run services itself so a background run keeps persisting with no page rendered.
if (playback)
{
    builder.Services.AddSingleton<ISessionStore, MemorySessionStore>();
}
else
{
    builder.Services.AddSingleton<ISessionStore>(sp => new JsonSessionStore(
        JsonSessionStore.ResolveFolder(sp.GetRequiredService<IConfiguration>()["Climber:SessionsPath"]),
        sp.GetService<ILogger<JsonSessionStore>>()));
}
builder.Services.AddSingleton<SessionCoordinator>();

// Project status page: git history is read live, coverage/test status comes from the
// committed artifact under status/.
builder.Services.AddSingleton<IProjectStatusService, ProjectStatusService>();

// Pre-flight checks for a climb target (folder exists, is a git repo, tools on PATH).
builder.Services.AddSingleton<IClimbPreflight, ClimbPreflight>();

// Provider surfaces. Configuration resolves to an ordered registry; the first provider is
// primary and every later one is an approval-gated fallback. A short-timeout client keeps a
// stalled API from hanging a page render; parsing is defensive so a bad key is never fatal.
builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
builder.Services.AddSingleton<IProviderRegistry>(sp => ProviderRegistry.FromConfiguration(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<HttpClient>()));

// PIN gate: unlock persists for the browser session and slides with activity,
// expiring after Climber:SessionMinutes (default 30) of inactivity.
builder.Services.AddSingleton<IClimberAuth>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var pin = ClimberBinding.ResolvePin(config["Climber:Pin"]);
    var minutes = int.TryParse(config["Climber:SessionMinutes"], out var parsed) && parsed > 0 ? parsed : 30;
    return new ClimberAuth(pin, TimeSpan.FromMinutes(minutes));
});

// Listen on all interfaces so the dashboard is reachable from a phone on the same Wi-Fi.
// ASPNETCORE_URLS (or DOTNET_URLS) still wins when explicitly set. The shipped default PIN
// is loopback-only: a real PIN is required before the app is exposed on the LAN.
var configuredUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
    ?? Environment.GetEnvironmentVariable("DOTNET_URLS");
var binding = ClimberBinding.Resolve(configuredUrls, builder.Configuration["Climber:Pin"]);
builder.WebHost.UseUrls(binding.Urls);

var app = builder.Build();

if (binding.DowngradedToLoopback)
{
    app.Logger.LogWarning(
        "Climber:Pin is the default; listening on {Url} only. Set a real PIN to expose the dashboard on the LAN.",
        binding.Urls);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Plain HTTP on the LAN: no HTTPS redirect so phones can connect directly.
app.MapStaticAssets();

// Development only: the live loop's staleness check. Not a product surface, and never mapped
// outside Development — the refresh script is inert when this 404s.
if (DevLoop.IsEnabled(app.Environment.IsDevelopment()))
{
    app.MapGet(DevLoop.StampPath, (IWebHostEnvironment environment) => DevLoop.Stamp(environment.ContentRootPath));
}
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

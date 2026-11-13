using ClimberWeb.Components;
using ClimberWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Command execution + hill-climbing loop. Registered as a singleton so a climb keeps
// running (and stays observable) across page reloads and mobile reconnects.
builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
builder.Services.AddSingleton<IHillClimberService, HillClimberService>();

// Listen on all interfaces so the dashboard is reachable from a phone on the same Wi-Fi.
// ASPNETCORE_URLS (or DOTNET_URLS) still wins when explicitly set.
var configuredUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
    ?? Environment.GetEnvironmentVariable("DOTNET_URLS");
builder.WebHost.UseUrls(configuredUrls ?? "http://0.0.0.0:5179");

var app = builder.Build();

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

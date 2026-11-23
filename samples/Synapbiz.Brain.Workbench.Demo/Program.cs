using Synapbiz.Brain.Workbench.Blazor;
using Synapbiz.Brain.Workbench.Demo;
using Synapbiz.Brain.Workbench.Demo.Components;

// The demo always runs as a development host, so that the static assets of the panels are served straight from the build output.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, EnvironmentName = Environments.Development });
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// One session per browser circuit: the panels keep the reviewer's work in it and nowhere else.
builder.Services.AddScoped<WorkbenchSession>();
builder.Services.AddScoped<FlowWorkbenchSession>();
builder.Services.AddSingleton(new DemoRules());
builder.Services.AddSingleton(new DemoFlows());

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.RunAsync().ConfigureAwait(false);

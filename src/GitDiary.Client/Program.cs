using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using GitDiary.Client;
using GitDiary.Client.Services;
using GitDiary.Client.Stores;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Services
builder.Services.AddScoped<GitHubApiClient>();
builder.Services.AddScoped<DiaryRepository>();
builder.Services.AddSingleton<IndexedDbRepository>();
builder.Services.AddSingleton<SearchService>();
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<OnlineSyncCoordinator>();

// Stores
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddScoped<DiaryStore>();

var host = builder.Build();

// Load persisted UI language from localStorage before the first render so the
// Setup Wizard already shows in the user's preferred language.
await host.Services.GetRequiredService<LocalizationService>().InitializeAsync();

// Paint the persisted theme (or the OS preference for System mode) and start
// watching the media query so live OS changes propagate.
await host.Services.GetRequiredService<ThemeService>().InitializeAsync();

await host.RunAsync();

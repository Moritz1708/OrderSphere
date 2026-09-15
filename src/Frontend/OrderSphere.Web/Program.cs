using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.JSInterop;
using MudBlazor.Services;
using OrderSphere.Web.Auth;
using OrderSphere.Web.Services;
using Polly;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<OrderSphere.Web.App>("#app");

// Localization — IStringLocalizer<AppStrings> resolves against Resources/AppStrings*.resx.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// CSRF protection service. Singleton so IHttpClientFactory handler scopes resolve
// the same instance that BffAuthStateProvider writes to.
builder.Services.AddSingleton<CsrfTokenService>();

// HTTP message handlers (transient so the HttpClientFactory places one per client).
builder.Services.AddTransient<AntiforgeryDelegatingHandler>();
builder.Services.AddTransient<LoggingHandler>();

var apiBaseAddress = new Uri(builder.HostEnvironment.BaseAddress);

// "api" — all REST calls. Handlers run in registration order (outer→inner): logging,
// resilience (retry/circuit-breaker/timeout), CSRF. Only idempotent GETs are retried;
// mutations are never replayed.
var apiClient = builder.Services.AddHttpClient("api", client => client.BaseAddress = apiBaseAddress);
apiClient.AddHttpMessageHandler<LoggingHandler>();
apiClient.AddResilienceHandler("api-pipeline", ConfigureResilience);
apiClient.AddHttpMessageHandler<AntiforgeryDelegatingHandler>();

// "advisor" — chat/SSE. Deliberately no resilience: a request timeout would abort the
// streamed token response.
var advisorClient = builder.Services.AddHttpClient("advisor", client => client.BaseAddress = apiBaseAddress);
advisorClient.AddHttpMessageHandler<LoggingHandler>();
advisorClient.AddHttpMessageHandler<AntiforgeryDelegatingHandler>();

// Inject the "api" client wherever a plain HttpClient is requested.
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("api"));

// Auth — BFF cookie pattern: state is derived from /bff/user
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, BffAuthStateProvider>();

// API clients
builder.Services.AddScoped<ICatalogClient, CatalogClient>();
builder.Services.AddScoped<IOrderingClient, OrderingClient>();
builder.Services.AddScoped<IUserProfileClient, UserProfileClient>();
builder.Services.AddScoped<IAdvisorClient, AdvisorClient>();
builder.Services.AddScoped<IInvoicingClient, InvoicingClient>();

// Admin clients
builder.Services.AddScoped<IAdminCatalogClient, AdminCatalogClient>();
builder.Services.AddScoped<IAdminOrderingClient, AdminOrderingClient>();
builder.Services.AddScoped<IAdminUserClient, AdminUserClient>();
builder.Services.AddScoped<IAdminInvoicingClient, AdminInvoicingClient>();

// Application state
builder.Services.AddScoped<CartState>();
builder.Services.AddScoped<NotificationHubClient>();
builder.Services.AddSingleton<ThemeState>();
builder.Services.AddScoped<IMotionService, MotionService>();

// MudBlazor
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = false;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 6000;
    config.SnackbarConfiguration.HideTransitionDuration = 180;
    config.SnackbarConfiguration.ShowTransitionDuration = 220;
    config.SnackbarConfiguration.SnackbarVariant = MudBlazor.Variant.Filled;
});

var host = builder.Build();

// Apply the user's stored language before the first render. Reading localStorage requires
// the built host's JS runtime; an unknown/absent value falls back to the default culture.
var js = host.Services.GetRequiredService<IJSRuntime>();
var stored = await js.InvokeAsync<string?>("localStorage.getItem", SupportedCultures.StorageKey);
var culture = new CultureInfo(SupportedCultures.Normalize(stored));
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// Apply the theme that theme-boot.js already painted, so MudThemeProvider emits
// the matching palette on its first render instead of correcting itself after.
try
{
    var isDark = await js.InvokeAsync<bool>("osBoot.isDark");
    host.Services.GetRequiredService<ThemeState>().SetDarkMode(isDark);
}
catch
{
    // Boot script blocked or storage unavailable — light mode is the default.
}

// Apply the user's stored display currency before the first render. The rate table is fetched
// once from the BFF; conversion is presentation-only. Any failure falls back to the base currency.
var storedCurrency = SupportedCurrencies.Normalize(
    await js.InvokeAsync<string?>("localStorage.getItem", SupportedCurrencies.StorageKey));
try
{
    using var ratesHttp = new HttpClient { BaseAddress = apiBaseAddress };
    var table = await ratesHttp.GetFromJsonAsync<ExchangeRatesResponse>("bff/exchange-rates");
    var rate = table?.Rates is { } rates && rates.TryGetValue(storedCurrency, out var r) ? r : 1m;
    Formatting.SetDisplayCurrency(storedCurrency, rate);
}
catch
{
    // BFF unreachable or no rate table — render in the base currency (rate 1).
    Formatting.SetDisplayCurrency(SupportedCurrencies.Default, 1m);
}

await host.RunAsync();

// Resilience pipeline for the "api" client: retry idempotent GETs on server errors,
// trip a circuit breaker on sustained failures, and cap each attempt at 30s.
static void ConfigureResilience(ResiliencePipelineBuilder<HttpResponseMessage> pipeline)
{
    pipeline.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromMilliseconds(300),
        // The request method is unknown for transport exceptions, so those are not
        // retried — a non-idempotent mutation must never be replayed.
        ShouldHandle = static args =>
        {
            var method = args.Outcome.Result?.RequestMessage?.Method;
            var idempotent = method == HttpMethod.Get || method == HttpMethod.Head;
            return ValueTask.FromResult(
                idempotent && args.Outcome.Result is { } response && (int)response.StatusCode >= 500);
        },
    });

    pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        MinimumThroughput = 10,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(15),
    });

    pipeline.AddTimeout(TimeSpan.FromSeconds(30));
}

// Shape of GET /bff/exchange-rates: base currency plus rate-per-base for every supported currency.
internal sealed record ExchangeRatesResponse(string BaseCurrency, Dictionary<string, decimal> Rates);

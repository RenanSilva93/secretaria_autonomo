using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using SecretariaAutonomo.Web;
using SecretariaAutonomo.Web.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

var apiBaseUrl = new Uri(builder.Configuration["ApiBaseUrl"] ?? throw new InvalidOperationException("Configure ApiBaseUrl em wwwroot/appsettings.json"));

// No WebAssembly só existe um usuário por aba, então os serviços de sessão podem ser singletons.
builder.Services.AddSingleton(sp => new Sessao(sp.GetRequiredService<IJSRuntime>(), new HttpClient { BaseAddress = apiBaseUrl }));
builder.Services.AddSingleton(sp => new HttpClient(new AutenticacaoHandler(sp.GetRequiredService<Sessao>()) { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = apiBaseUrl,
});
builder.Services.AddSingleton<ApiClient>();
builder.Services.AddSingleton<MonitorAgendamentos>();
builder.Services.AddSingleton<NotificacoesPush>();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<ProvedorAutenticacao>();
builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<ProvedorAutenticacao>());

var ptBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = ptBr;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = ptBr;

await builder.Build().RunAsync();

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SecretariaAutonomo.Api;
using SecretariaAutonomo.Api.Endpoints;
using SecretariaAutonomo.Application;
using SecretariaAutonomo.Infrastructure;
using SecretariaAutonomo.Infrastructure.Identidade;
using SecretariaAutonomo.Infrastructure.Persistencia;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Login com bearer tokens do ASP.NET Core Identity (o front guarda e renova os tokens).
builder.Services.AddAuthentication(IdentityConstants.BearerScheme)
    .AddBearerToken(IdentityConstants.BearerScheme);
builder.Services.AddAuthorization();
builder.Services.AddIdentityCore<Usuario>().AddSignInManager().AddDefaultTokenProviders();

// Links de "esqueci minha senha" valem 1 hora.
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.Secao));
builder.Services.AddMemoryCache();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorDeErros>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origens").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

// A página pública não tem login: limita requisições por IP para evitar abuso.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(Program.LimitePublico, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Em desenvolvimento, aplica as migrations e cria o tenant de exemplo automaticamente.
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DadosDemonstracao.CriarSeNecessarioAsync(db, scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>());
}

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Secretária para Autônomo — API");
app.MapAuth();
app.MapPainel();
app.MapPublico();
app.MapWhatsAppWebhook();

app.Run();

public partial class Program
{
    public const string LimitePublico = "publico";
}

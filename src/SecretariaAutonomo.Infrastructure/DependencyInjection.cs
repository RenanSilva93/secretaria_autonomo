using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecretariaAutonomo.Application.Abstracoes;
using SecretariaAutonomo.Infrastructure.Email;
using SecretariaAutonomo.Infrastructure.Identidade;
using SecretariaAutonomo.Infrastructure.Persistencia;
using SecretariaAutonomo.Infrastructure.Push;
using Lib.Net.Http.WebPush;
using SecretariaAutonomo.Infrastructure.WhatsApp;

namespace SecretariaAutonomo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddDbContext<AppDbContext>(o =>
            o.UseNpgsql(configuration.GetConnectionString("Postgres")));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<Usuario>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddErrorDescriber<ErrosIdentityPtBr>()
            .AddClaimsPrincipalFactory<UsuarioClaimsFactory>();

        services.Configure<WhatsAppOptions>(configuration.GetSection(WhatsAppOptions.Secao));
        services.AddHttpClient<IWhatsAppClient, WhatsAppCloudClient>((sp, c) =>
            c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<WhatsAppOptions>>().Value.BaseUrl));
        services.AddHostedService<LembreteWorker>();

        // Chaves que assinam os tokens de login e de "esqueci minha senha". Ficam no banco para sobreviver a
        // reinícios e deploys; sem isso, cada reinício do servidor deslogaria todo mundo.
        services.AddDataProtection()
            .SetApplicationName("SecretariaAutonomo")
            .PersistKeysToDbContext<AppDbContext>();

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Secao));
        services.AddScoped<IEnviadorEmail>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<EmailOptions>>().Value.SmtpHost)
                ? ActivatorUtilities.CreateInstance<EnviadorSomenteLog>(sp)
                : ActivatorUtilities.CreateInstance<EnviadorSmtp>(sp));
        services.AddSingleton<FilaEmails>();
        services.AddSingleton<IFilaEmails>(sp => sp.GetRequiredService<FilaEmails>());
        services.AddHostedService<EmailWorker>();

        services.AddSingleton<FilaConfirmacoes>();
        services.AddSingleton<IFilaConfirmacoes>(sp => sp.GetRequiredService<FilaConfirmacoes>());
        services.AddHostedService<ConfirmacaoWorker>();

        services.Configure<PushOptions>(configuration.GetSection(PushOptions.Secao));
        services.AddHttpClient<PushServiceClient>();
        services.AddScoped<IEnviadorPush, EnviadorWebPush>();
        services.AddSingleton<FilaAvisosPush>();
        services.AddSingleton<IFilaAvisosPush>(sp => sp.GetRequiredService<FilaAvisosPush>());
        services.AddHostedService<PushWorker>();

        return services;
    }
}

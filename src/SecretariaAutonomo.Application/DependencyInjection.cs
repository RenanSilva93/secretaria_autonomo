using Microsoft.Extensions.DependencyInjection;
using SecretariaAutonomo.Application.Agenda;
using SecretariaAutonomo.Application.Financeiro;
using SecretariaAutonomo.Application.WhatsApp;

namespace SecretariaAutonomo.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AgendaService>();
        services.AddScoped<FinanceiroService>();
        services.AddScoped<LembreteService>();
        services.AddScoped<ProcessadorMensagens>();
        services.AddScoped<NotificacoesService>();
        services.AddScoped<Push.AvisosPushService>();
        services.AddScoped<Chatbot.ChatbotService>();
        return services;
    }
}
